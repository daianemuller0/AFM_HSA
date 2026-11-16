using System.Data;
using DuckDB.NET.Data;

namespace AfmHsa.Data;

/// <summary>
/// Camada de dados no padrão recomendado pela equipe de melhoria (Edson):
/// DuckDB como MOTOR sobre arquivos Parquet numa pasta de rede.
///
/// Cada gravação (adição/edição) cria um pequeno arquivo .parquet novo dentro
/// da subpasta da entidade (ex.: clientes/). Nada de "um único banco na rede"
/// (que com SQLite trava e fica lento). Na leitura, o DuckDB lê a pasta inteira
/// e CONSOLIDA: para cada id, mantém a versão mais recente (_ts) e ignora os
/// registros marcados como apagados (_deleted).
///
/// Como cada operação abre um DuckDB em memória (Data Source=:memory:) e só
/// toca os Parquet, não há arquivo de banco compartilhado sendo travado —
/// vários usuários podem gravar ao mesmo tempo, cada um no seu arquivinho.
/// </summary>
public sealed class ParquetStore
{
    public string Folder { get; }

    public ParquetStore(string folder)
    {
        Folder = Path.GetFullPath(folder);
        try
        {
            Directory.CreateDirectory(Folder);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Não foi possível acessar a pasta de dados '{Folder}'. " +
                "Verifique a chave \"Data:Folder\" no appsettings e se o caminho de rede " +
                "está acessível a partir desta máquina.", ex);
        }
    }

    private string EntityDir(string entity)
    {
        var dir = Path.Combine(Folder, entity);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static DuckDBConnection Open()
    {
        var conn = new DuckDBConnection("Data Source=:memory:");
        conn.Open();
        return conn;
    }

    private static void AddParam(IDbCommand cmd, object? value)
    {
        var p = cmd.CreateParameter();
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    // Caminho no formato que o DuckDB entende (barras normais, mesmo no Windows).
    private static string Duck(string path) => path.Replace('\\', '/');

    /// <summary>Grava uma linha como um novo arquivo Parquet na subpasta da entidade.</summary>
    public void WriteRow(string entity, IReadOnlyList<KeyValuePair<string, object?>> row, bool deleted = false)
    {
        using var conn = Open();

        var colDefs = string.Join(", ", row.Select(kv => $"\"{kv.Key}\" VARCHAR")) + ", _ts BIGINT, _deleted BOOLEAN";
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"CREATE TABLE t ({colDefs});";
            cmd.ExecuteNonQuery();
        }

        var placeholders = string.Join(", ", row.Select(_ => "?")) + ", ?, ?";
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"INSERT INTO t VALUES ({placeholders});";
            foreach (var kv in row) AddParam(cmd, kv.Value);
            AddParam(cmd, DateTime.UtcNow.Ticks);
            AddParam(cmd, deleted);
            cmd.ExecuteNonQuery();
        }

        var dir = EntityDir(entity);
        var fileName = $"{DateTime.UtcNow.Ticks:D19}_{Guid.NewGuid():N}.parquet";
        var full = Duck(Path.Combine(dir, fileName));
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"COPY t TO '{full}' (FORMAT PARQUET);";
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Grava VÁRIAS linhas de uma vez, num ÚNICO arquivo Parquet e uma só conexão.
    /// Muito mais rápido que WriteRow em loop (ex.: seed inicial) — evita abrir
    /// centenas de conexões e escrever centenas de arquivos na pasta de rede.
    /// As linhas podem ter colunas diferentes; usa-se a união (faltantes = NULL).
    /// </summary>
    public void WriteBatch(string entity, IReadOnlyList<IReadOnlyList<KeyValuePair<string, object?>>> rows, bool deleted = false)
    {
        if (rows.Count == 0) return;

        // União das colunas, preservando a ordem de primeira aparição.
        var cols = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
            foreach (var kv in row)
                if (seen.Add(kv.Key)) cols.Add(kv.Key);

        using var conn = Open();

        var colDefs = string.Join(", ", cols.Select(c => $"\"{c}\" VARCHAR")) + ", _ts BIGINT, _deleted BOOLEAN";
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"CREATE TABLE t ({colDefs});";
            cmd.ExecuteNonQuery();
        }

        var placeholders = string.Join(", ", cols.Select(_ => "?")) + ", ?, ?";
        var ts = DateTime.UtcNow.Ticks;
        foreach (var row in rows)
        {
            var map = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in row) map[kv.Key] = kv.Value;

            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"INSERT INTO t VALUES ({placeholders});";
            foreach (var c in cols) AddParam(cmd, map.TryGetValue(c, out var v) ? v : null);
            AddParam(cmd, ts);
            AddParam(cmd, deleted);
            cmd.ExecuteNonQuery();
        }

        var dir = EntityDir(entity);
        var fileName = $"{DateTime.UtcNow.Ticks:D19}_{Guid.NewGuid():N}.parquet";
        var full = Duck(Path.Combine(dir, fileName));
        using (var copy = conn.CreateCommand())
        {
            copy.CommandText = $"COPY t TO '{full}' (FORMAT PARQUET);";
            copy.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Lê a entidade já consolidada: versão mais recente por id, sem os apagados.
    /// Retorna vazio se ainda não houver nenhum arquivo (primeira execução).
    /// </summary>
    public List<T> ReadLatest<T>(string entity, string selectCols, Func<IDataReader, T> map, string orderBy = "")
    {
        // O resultado fica guardado enquanto a pasta não muda: a mesma leitura
        // de novo (outra tela, o mesmo dado) só confere a lista de arquivos, em
        // vez de abrir todos eles. Qualquer gravação, de qualquer pessoa, cria um
        // arquivo novo e a próxima leitura vai aos arquivos. Os objetos são
        // montados de novo a cada leitura (quem altera um não altera o guardado).
        var assinatura = Assinatura(entity);
        if (assinatura.EndsWith("|0|", StringComparison.Ordinal)) return new List<T>();
        var chave = $"{Folder}|{entity}|{selectCols}|{orderBy}";
        if (!_lidos.TryGetValue(chave, out var guardado) || guardado.Assinatura != assinatura)
        {
            guardado = (assinatura, Consultar(entity, selectCols, orderBy));
            _lidos[chave] = guardado;
        }
        var list = new List<T>(guardado.Tabela.Rows.Count);
        using var r = guardado.Tabela.CreateDataReader();
        while (r.Read()) list.Add(map(r));
        return list;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Assinatura, DataTable Tabela)> _lidos = new();

    private DataTable Consultar(string entity, string selectCols, string orderBy)
    {
        var dir = EntityDir(entity);
        var glob = Duck(Path.Combine(dir, "*.parquet"));
        using var conn = Open();

        // Evolução de esquema: arquivos antigos podem não ter colunas novas.
        // union_by_name junta esquemas diferentes entre arquivos, e as colunas
        // pedidas que não existirem em NENHUM arquivo viram NULL no SELECT.
        var presentes = ColunasDa(entity, conn, $"'{glob}'");

        var cols = string.Join(", ", selectCols.Split(',')
            .Select(c => c.Trim())
            .Select(c => presentes.Contains(c) ? c : $"NULL AS {c}"));

        var order = string.IsNullOrWhiteSpace(orderBy) ? "" : $" ORDER BY {orderBy}";
        var sql = $@"
SELECT {cols}
FROM (
    SELECT *, row_number() OVER (PARTITION BY id ORDER BY _ts DESC) AS _rn
    FROM read_parquet('{glob}', union_by_name=true)
)
WHERE _rn = 1 AND NOT _deleted{order};";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        using var r = cmd.ExecuteReader();
        var tabela = new DataTable();
        tabela.Load(r);
        return tabela;
    }

    /// <summary>
    /// TODAS as versões gravadas da entidade (não só a mais recente), em ordem
    /// de gravação, com o instante (_ts) e se era uma exclusão. Nada se perde
    /// numa regravação: é daqui que se recupera o que foi gravado por cima.
    /// </summary>
    public List<(T Linha, long Ts, bool Apagado, string Arquivo)> ReadHistory<T>(string entity, string selectCols, Func<IDataReader, T> map,
        IReadOnlyCollection<string>? soArquivos = null)
    {
        var dir = EntityDir(entity);
        if (soArquivos is { Count: 0 } || !Directory.EnumerateFiles(dir, "*.parquet").Any())
            return new List<(T, long, bool, string)>();

        // a pasta inteira, ou só os arquivos pedidos (ler o que interessa, e não tudo de novo)
        var fonte = soArquivos is null
            ? $"'{Duck(Path.Combine(dir, "*.parquet"))}'"
            : "[" + string.Join(", ", soArquivos.Select(f => $"'{Duck(f).Replace("'", "''")}'")) + "]";
        using var conn = Open();

        // a pasta inteira: as colunas guardadas; só alguns arquivos: as deles
        var presentes = soArquivos is null ? ColunasDa(entity, conn, fonte) : Descrever(conn, fonte);

        var lista = selectCols.Split(',').Select(c => c.Trim()).ToList();
        var cols = string.Join(", ", lista.Select(c => presentes.Contains(c) ? c : $"NULL AS {c}"));
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {cols}, _ts, _deleted, filename FROM read_parquet({fonte}, union_by_name=true, filename=true) ORDER BY _ts;";
        using var r = cmd.ExecuteReader();
        var saida = new List<(T, long, bool, string)>();
        var n = lista.Count;
        while (r.Read())
            saida.Add((map(r), r.IsDBNull(n) ? 0 : Convert.ToInt64(r.GetValue(n)), !r.IsDBNull(n + 1) && Convert.ToBoolean(r.GetValue(n + 1)),
                r.IsDBNull(n + 2) ? "" : r.GetString(n + 2)));
        return saida;
    }

    // As colunas que existem nos arquivos da entidade (evolução de esquema). Descobrir
    // isso abre todos os arquivos — tanto quanto a própria leitura —, então fica
    // guardado enquanto a pasta não muda (mesma quantidade de arquivos e o mesmo mais novo).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Assinatura, HashSet<string> Colunas)> _colunas = new();

    private HashSet<string> ColunasDa(string entity, DuckDBConnection conn, string fonte)
    {
        var chave = $"{Folder}|{entity}";
        var assinatura = Assinatura(entity);
        if (_colunas.TryGetValue(chave, out var c) && c.Assinatura == assinatura) return c.Colunas;
        var colunas = Descrever(conn, fonte);
        _colunas[chave] = (assinatura, colunas);
        return colunas;
    }

    private static HashSet<string> Descrever(DuckDBConnection conn, string fonte)
    {
        var presentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var describe = conn.CreateCommand();
        describe.CommandText = $"DESCRIBE SELECT * FROM read_parquet({fonte}, union_by_name=true);";
        using var dr = describe.ExecuteReader();
        while (dr.Read()) presentes.Add(dr.GetString(0));
        return presentes;
    }

    /// <summary>
    /// Um retrato barato da pasta da entidade (quantos arquivos e o nome do
    /// mais novo, que começa pelo instante da gravação): muda a cada gravação,
    /// exclusão ou restauração. Serve para não reler a pasta quando nada mudou.
    /// </summary>
    public string Assinatura(string entity)
    {
        var dir = EntityDir(entity);
        var n = 0;
        var maior = "";
        foreach (var f in Directory.EnumerateFiles(dir, "*.parquet"))
        {
            n++;
            var nome = Path.GetFileName(f);
            if (string.CompareOrdinal(nome, maior) > 0) maior = nome;
        }
        return $"{Folder}|{entity}|{n}|{maior}";
    }

    /// <summary>
    /// Devolve à entidade os Parquet de um backup (.zip com os arquivos da
    /// pasta dela). Junta com o que já existe: arquivo de mesmo nome não é
    /// tocado, e por id vale a gravação mais recente. Devolve quantos voltaram.
    /// </summary>
    public int RestaurarDe(string entity, string zip)
    {
        var dir = EntityDir(entity);
        var n = 0;
        using var arquivo = System.IO.Compression.ZipFile.OpenRead(zip);
        foreach (var e in arquivo.Entries)
        {
            var nome = Path.GetFileName(e.FullName);
            if (!nome.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase) || nome != e.FullName) continue;
            var destino = Path.Combine(dir, nome);
            if (File.Exists(destino)) continue;
            System.IO.Compression.ZipFileExtensions.ExtractToFile(e, destino);
            n++;
        }
        return n;
    }

    /// <summary>A subpasta da entidade (onde ficam os Parquet dela).</summary>
    public string PastaDa(string entity) => EntityDir(entity);

    public bool IsEmpty(string entity)
    {
        var dir = EntityDir(entity);
        return !Directory.EnumerateFiles(dir, "*.parquet").Any();
    }

    /// <summary>
    /// Apaga todos os Parquet da entidade (usado pela importação em modo "substituir").
    /// </summary>
    public void Clear(string entity)
    {
        var dir = EntityDir(entity);
        foreach (var f in Directory.EnumerateFiles(dir, "*.parquet"))
            File.Delete(f);
    }
}
