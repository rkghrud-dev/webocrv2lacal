using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace KeywordOcr.App.Services;

internal sealed class ProductDatabase : IDisposable
{
    private readonly SqliteConnection _conn;

    public ProductDatabase(string dbPath)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        InitSchema();
    }

    private void InitSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS products (
    gs_code         TEXT PRIMARY KEY,
    product_name    TEXT NOT NULL,
    first_seen      TEXT NOT NULL,
    last_processed  TEXT,
    source_file     TEXT
);

CREATE TABLE IF NOT EXISTS upload_history (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    gs_code         TEXT NOT NULL,
    product_name    TEXT NOT NULL,
    market          TEXT NOT NULL,
    status          TEXT NOT NULL,
    product_id      TEXT,
    error           TEXT,
    uploaded_at     TEXT NOT NULL,
    source_file     TEXT,
    UNIQUE(gs_code, market, uploaded_at)
);

CREATE TABLE IF NOT EXISTS work_sessions (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    session_date    TEXT NOT NULL,
    source_file     TEXT NOT NULL,
    product_count   INTEGER NOT NULL DEFAULT 0,
    output_root     TEXT,
    zip_path        TEXT,
    status          TEXT NOT NULL DEFAULT 'STARTED',
    created_at      TEXT NOT NULL,
    completed_at    TEXT
);

CREATE TABLE IF NOT EXISTS no_image_products (
    gs_code         TEXT NOT NULL,
    product_name    TEXT NOT NULL,
    session_id      INTEGER,
    added_at        TEXT NOT NULL,
    resolved        INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY(gs_code, session_id)
);

CREATE INDEX IF NOT EXISTS idx_upload_gs ON upload_history(gs_code);
CREATE INDEX IF NOT EXISTS idx_upload_market ON upload_history(market);
CREATE INDEX IF NOT EXISTS idx_upload_date ON upload_history(uploaded_at);
CREATE INDEX IF NOT EXISTS idx_session_date ON work_sessions(session_date);
";
        cmd.ExecuteNonQuery();
    }

    public void UpsertProduct(string gsCode, string productName, string? sourceFile = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO products (gs_code, product_name, first_seen, last_processed, source_file)
VALUES ($gs, $name, $now, $now, $src)
ON CONFLICT(gs_code) DO UPDATE SET
    product_name = $name,
    last_processed = $now,
    source_file = COALESCE($src, source_file)";
        cmd.Parameters.AddWithValue("$gs", gsCode);
        cmd.Parameters.AddWithValue("$name", productName);
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$src", (object?)sourceFile ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public bool IsProductProcessed(string gsCode)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM products WHERE gs_code = $gs AND last_processed IS NOT NULL";
        cmd.Parameters.AddWithValue("$gs", gsCode);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    public string? GetLastProcessedDate(string gsCode)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT last_processed FROM products WHERE gs_code = $gs";
        cmd.Parameters.AddWithValue("$gs", gsCode);
        return cmd.ExecuteScalar()?.ToString();
    }

    public void RecordUpload(string gsCode, string productName, string market, string status,
        string? productId = null, string? error = null, string? sourceFile = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT OR IGNORE INTO upload_history (gs_code, product_name, market, status, product_id, error, uploaded_at, source_file)
VALUES ($gs, $name, $market, $status, $pid, $err, $now, $src)";
        cmd.Parameters.AddWithValue("$gs", gsCode);
        cmd.Parameters.AddWithValue("$name", productName);
        cmd.Parameters.AddWithValue("$market", market);
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$pid", (object?)productId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$err", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$src", (object?)sourceFile ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public List<UploadHistoryRow> GetUploadHistory(string? dateFilter = null, string? marketFilter = null,
        string? gsCodeFilter = null, int limit = 500)
    {
        var rows = new List<UploadHistoryRow>();
        using var cmd = _conn.CreateCommand();
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(dateFilter))
        {
            where.Add("uploaded_at LIKE $date");
            cmd.Parameters.AddWithValue("$date", dateFilter + "%");
        }
        if (!string.IsNullOrWhiteSpace(marketFilter))
        {
            where.Add("market = $market");
            cmd.Parameters.AddWithValue("$market", marketFilter);
        }
        if (!string.IsNullOrWhiteSpace(gsCodeFilter))
        {
            where.Add("gs_code LIKE $gs");
            cmd.Parameters.AddWithValue("$gs", "%" + gsCodeFilter + "%");
        }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        cmd.CommandText = $@"
SELECT id, gs_code, product_name, market, status, product_id, error, uploaded_at, source_file
FROM upload_history {whereClause}
ORDER BY uploaded_at DESC
LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new UploadHistoryRow
            {
                Id = reader.GetInt64(0),
                GsCode = reader.GetString(1),
                ProductName = reader.GetString(2),
                Market = reader.GetString(3),
                Status = reader.GetString(4),
                ProductId = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Error = reader.IsDBNull(6) ? "" : reader.GetString(6),
                UploadedAt = reader.GetString(7),
                SourceFile = reader.IsDBNull(8) ? "" : reader.GetString(8),
            });
        }
        return rows;
    }

    public int CreateWorkSession(string sourceFile, int productCount, string? outputRoot = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO work_sessions (session_date, source_file, product_count, output_root, created_at)
VALUES ($date, $src, $cnt, $out, $now);
SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$date", DateTime.Now.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$src", sourceFile);
        cmd.Parameters.AddWithValue("$cnt", productCount);
        cmd.Parameters.AddWithValue("$out", (object?)outputRoot ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void CompleteWorkSession(int sessionId, string status, string? zipPath = null, string? outputRoot = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
UPDATE work_sessions SET status = $status, completed_at = $now,
    zip_path = COALESCE($zip, zip_path),
    output_root = COALESCE($out, output_root)
WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("$zip", (object?)zipPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$out", (object?)outputRoot ?? DBNull.Value);
    }

    public List<WorkSessionRow> GetWorkSessions(int limit = 100)
    {
        var rows = new List<WorkSessionRow>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT id, session_date, source_file, product_count, output_root, zip_path, status, created_at, completed_at
FROM work_sessions ORDER BY created_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new WorkSessionRow
            {
                Id = reader.GetInt32(0),
                SessionDate = reader.GetString(1),
                SourceFile = reader.GetString(2),
                ProductCount = reader.GetInt32(3),
                OutputRoot = reader.IsDBNull(4) ? "" : reader.GetString(4),
                ZipPath = reader.IsDBNull(5) ? "" : reader.GetString(5),
                Status = reader.GetString(6),
                CreatedAt = reader.GetString(7),
                CompletedAt = reader.IsDBNull(8) ? "" : reader.GetString(8),
            });
        }
        return rows;
    }

    public void AddNoImageProduct(string gsCode, string productName, int sessionId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT OR IGNORE INTO no_image_products (gs_code, product_name, session_id, added_at)
VALUES ($gs, $name, $sid, $now)";
        cmd.Parameters.AddWithValue("$gs", gsCode);
        cmd.Parameters.AddWithValue("$name", productName);
        cmd.Parameters.AddWithValue("$sid", sessionId);
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    public void ResolveNoImageProduct(string gsCode, int sessionId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE no_image_products SET resolved = 1 WHERE gs_code = $gs AND session_id = $sid";
        cmd.Parameters.AddWithValue("$gs", gsCode);
        cmd.Parameters.AddWithValue("$sid", sessionId);
        cmd.ExecuteNonQuery();
    }

    public List<NoImageProductRow> GetUnresolvedNoImageProducts(int? sessionId = null)
    {
        var rows = new List<NoImageProductRow>();
        using var cmd = _conn.CreateCommand();
        var where = "resolved = 0";
        if (sessionId.HasValue)
        {
            where += " AND session_id = $sid";
            cmd.Parameters.AddWithValue("$sid", sessionId.Value);
        }
        cmd.CommandText = $"SELECT gs_code, product_name, session_id, added_at FROM no_image_products WHERE {where} ORDER BY added_at DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new NoImageProductRow
            {
                GsCode = reader.GetString(0),
                ProductName = reader.GetString(1),
                SessionId = reader.GetInt32(2),
                AddedAt = reader.GetString(3),
            });
        }
        return rows;
    }

    public void UpdateUploadHistoryField(long id, string field, string value)
    {
        var allowed = new HashSet<string> { "status", "product_id", "error", "product_name" };
        if (!allowed.Contains(field)) return;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"UPDATE upload_history SET {field} = $val WHERE id = $id";
        cmd.Parameters.AddWithValue("$val", value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void DeleteUploadHistory(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM upload_history WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public Dictionary<string, List<string>> GetUploadedMarkets()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT gs_code, GROUP_CONCAT(DISTINCT market)
FROM upload_history WHERE status IN ('OK','SKIP_DUP')
GROUP BY gs_code";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var gs = reader.GetString(0);
            var markets = reader.GetString(1).Split(',', StringSplitOptions.RemoveEmptyEntries);
            result[gs] = new List<string>(markets);
        }
        return result;
    }

    public void Dispose() => _conn.Dispose();
}

internal sealed class UploadHistoryRow
{
    public long Id { get; set; }
    public string GsCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string Market { get; set; } = "";
    public string Status { get; set; } = "";
    public string ProductId { get; set; } = "";
    public string Error { get; set; } = "";
    public string UploadedAt { get; set; } = "";
    public string SourceFile { get; set; } = "";
}

internal sealed class WorkSessionRow
{
    public int Id { get; set; }
    public string SessionDate { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public int ProductCount { get; set; }
    public string OutputRoot { get; set; } = "";
    public string ZipPath { get; set; } = "";
    public string Status { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string CompletedAt { get; set; } = "";
}

internal sealed class NoImageProductRow
{
    public string GsCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int SessionId { get; set; }
    public string AddedAt { get; set; } = "";
}
