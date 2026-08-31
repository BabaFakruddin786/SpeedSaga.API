using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public record KycReviewRequest(string DocType, string Action, string? Reason);

public interface IKycAdminService
{
    Task<IReadOnlyList<object>> ListPendingAsync(int page, CancellationToken ct = default);
    Task<ApiResponse<object>> ReviewAsync(Guid playerId, KycReviewRequest req, CancellationToken ct = default);
    Task<(string Path, string ContentType)?> GetDocumentAsync(Guid playerId, string docType, CancellationToken ct = default);
}

public class KycAdminService : IKycAdminService
{
    readonly ISqlConnectionFactory _db;
    readonly KycDocumentStorage _storage;

    public KycAdminService(ISqlConnectionFactory db, KycDocumentStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<IReadOnlyList<object>> ListPendingAsync(int page, CancellationToken ct = default)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_AdminListPendingKyc", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PageNo", page < 1 ? 1 : page);
        cmd.Parameters.AddWithValue("@PageSize", 50);

        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        while (await rdr.ReadAsync(ct))
        {
            list.Add(new
            {
                PlayerId = rdr["PlayerId"].ToString(),
                Username = rdr["Username"]?.ToString(),
                ContactEmail = rdr["ContactEmail"]?.ToString(),
                ContactPhone = rdr["ContactPhone"]?.ToString(),
                AadhaarStatus = rdr["AadhaarStatus"]?.ToString(),
                PanStatus = rdr["PANStatus"]?.ToString(),
                BankStatus = rdr["BankStatus"]?.ToString(),
                IsFullyVerified = (bool)rdr["IsFullyVerified"],
                AadhaarMasked = rdr["AadhaarNumber"]?.ToString(),
                PanMasked = rdr["PANNumber"]?.ToString(),
                BankMasked = rdr["BankAccount"]?.ToString(),
                BankIfsc = rdr["BankIFSC"]?.ToString(),
                BankHolder = rdr["BankName"]?.ToString(),
                AadhaarNameOnCard = rdr["AadhaarNameOnCard"]?.ToString(),
                UpdatedAt = (DateTime)rdr["UpdatedAt"]
            });
        }
        return list;
    }

    public async Task<ApiResponse<object>> ReviewAsync(Guid playerId, KycReviewRequest req, CancellationToken ct = default)
    {
        var docType = req.DocType?.Trim() ?? "";
        var action = req.Action?.Trim() ?? "";
        if (docType is not ("Aadhaar" or "PAN" or "Bank"))
            return new ApiResponse<object>(false, "DocType must be Aadhaar, PAN, or Bank");
        if (action is not ("Approve" or "Reject"))
            return new ApiResponse<object>(false, "Action must be Approve or Reject");
        if (action == "Reject" && string.IsNullOrWhiteSpace(req.Reason))
            return new ApiResponse<object>(false, "Rejection reason is required");

        var status = await GetDocStatusAsync(playerId, docType, ct);
        if (status != "PendingReview")
            return new ApiResponse<object>(false, $"{docType} is not pending review (current: {status ?? "NotSubmitted"})");

        var finalStatus = action == "Approve" ? "Approved" : "Rejected";
        await SetKycDocumentAsync(playerId, docType, finalStatus, action == "Reject" ? req.Reason?.Trim() : null, ct);
        return new ApiResponse<object>(true, $"{docType} {finalStatus.ToLowerInvariant()}");
    }

    public async Task<(string Path, string ContentType)?> GetDocumentAsync(Guid playerId, string docType, CancellationToken ct = default)
    {
        docType = docType?.Trim() ?? "";
        var col = docType switch
        {
            "aadhaar" => "AadhaarDocPath",
            "pan" => "PanDocPath",
            "bank" => "BankDocPath",
            _ => null
        };
        if (col == null) return null;

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand($"SELECT {col} FROM PlayerKYC WHERE PlayerId = @PlayerId", cn);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        var result = await cmd.ExecuteScalarAsync(ct);
        var relative = result == DBNull.Value || result == null ? null : result.ToString();
        var full = _storage.ResolveFullPath(relative);
        if (full == null) return null;
        var contentType = _storage.GetContentType(full);
        return contentType == null ? null : (full, contentType);
    }

    async Task<string?> GetDocStatusAsync(Guid playerId, string docType, CancellationToken ct)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        var col = docType switch
        {
            "Aadhaar" => "AadhaarStatus",
            "PAN" => "PANStatus",
            _ => "BankStatus"
        };
        await using var cmd = new SqlCommand($"SELECT {col} FROM PlayerKYC WHERE PlayerId = @PlayerId", cn);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result == DBNull.Value || result == null ? null : result.ToString();
    }

    async Task SetKycDocumentAsync(Guid playerId, string docType, string status, string? rejectReason, CancellationToken ct)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("USP_SetKycDocument", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@DocType", docType);
        cmd.Parameters.AddWithValue("@DocNumber", DBNull.Value);
        cmd.Parameters.AddWithValue("@HolderName", DBNull.Value);
        cmd.Parameters.AddWithValue("@Ifsc", DBNull.Value);
        cmd.Parameters.AddWithValue("@Status", status);
        cmd.Parameters.AddWithValue("@NameOnAadhaar", DBNull.Value);
        cmd.Parameters.AddWithValue("@DocPath", DBNull.Value);
        cmd.Parameters.AddWithValue("@RejectReason", (object?)rejectReason ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
