using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface IWalletService
{
    Task<object?> GetWalletAsync(Guid playerId);
    Task<ApiResponse<object>> ProcessDepositAsync(Guid playerId, DepositRequest req);
    Task<ApiResponse<object>> ProcessDepositFromWebhookAsync(Guid playerId, long amountPaise, string orderId, string paymentId);
    Task<ApiResponse<object>> DeductEntryFeeAsync(Guid playerId, Guid sessionId, long feePaise);
    Task<ApiResponse<object>> WithdrawAsync(Guid playerId, WithdrawRequest req);
    Task<object?> GetTransactionsAsync(Guid playerId, string? type, int page);
    Task<bool> HasSufficientBalanceAsync(Guid playerId, long amountPaise);
    Task<ApiResponse<object>> DevDepositAsync(Guid playerId, long amountPaise);
}

public class WalletService : IWalletService
{
    private readonly ISqlConnectionFactory _db;
    private readonly IRazorpayService _razorpay;
    private readonly IWebHostEnvironment _env;
    private readonly INotificationService _notifications;

    public WalletService(ISqlConnectionFactory db, IRazorpayService razorpay, IWebHostEnvironment env, INotificationService notifications)
    {
        _db = db;
        _razorpay = razorpay;
        _env = env;
        _notifications = notifications;
    }

    public async Task<object?> GetWalletAsync(Guid playerId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetWallet", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return null;

        return new
        {
            BalanceRs = (long)rdr["BalancePaise"] / 100.0,
            BalancePaise = (long)rdr["BalancePaise"],
            DepositPaise = (long)rdr["DepositPaise"],
            WinningPaise = (long)rdr["WinningPaise"],
            WithdrawnPaise = (long)rdr["WithdrawnPaise"],
            BonusPaise = (long)rdr["BonusPaise"],
            AadhaarStatus = rdr["AadhaarStatus"].ToString(),
            PANStatus = rdr["PANStatus"].ToString(),
            BankStatus = rdr["BankStatus"].ToString(),
            IsFullyVerified = (bool)rdr["IsFullyVerified"]
        };
    }

    public async Task<bool> HasSufficientBalanceAsync(Guid playerId, long amountPaise)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(
            "SELECT BalancePaise FROM Wallets WHERE PlayerId = @PlayerId", cn);
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        var result = await cmd.ExecuteScalarAsync();
        return result != null && result != DBNull.Value && (long)result >= amountPaise;
    }

    public async Task<ApiResponse<object>> ProcessDepositAsync(Guid playerId, DepositRequest req)
    {
        if (!_razorpay.VerifySignature(req.RazorpayOrderId, req.RazorpayPaymentId, req.RazorpaySignature))
            return new ApiResponse<object>(false, "Payment signature verification failed.");

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_ProcessDeposit", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@AmountPaise", req.AmountPaise);
        cmd.Parameters.AddWithValue("@RazorpayOrderId", req.RazorpayOrderId);
        cmd.Parameters.AddWithValue("@RazorpayPaymentId", req.RazorpayPaymentId);

        var pRes = cmd.Parameters.Add("@Result", SqlDbType.Int);
        pRes.Direction = ParameterDirection.Output;
        var pMsg = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        pMsg.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();

        var r = (int)pRes.Value!;
        if (r == 1)
        {
            await _notifications.SendAsync(playerId, "Deposit successful",
                $"Rs {req.AmountPaise / 100.0:0} added to your wallet.", "Deposit");
            return new ApiResponse<object>(true, (string)pMsg.Value!, new { AmountRs = req.AmountPaise / 100.0 });
        }
        return new ApiResponse<object>(false, (string)pMsg.Value!);
    }

    public async Task<ApiResponse<object>> ProcessDepositFromWebhookAsync(Guid playerId, long amountPaise, string orderId, string paymentId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_ProcessDeposit", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@AmountPaise", amountPaise);
        cmd.Parameters.AddWithValue("@RazorpayOrderId", orderId);
        cmd.Parameters.AddWithValue("@RazorpayPaymentId", paymentId);

        var pRes = cmd.Parameters.Add("@Result", SqlDbType.Int);
        pRes.Direction = ParameterDirection.Output;
        var pMsg = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        pMsg.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();

        var r = (int)pRes.Value!;
        if (r == 1)
        {
            await _notifications.SendAsync(playerId, "Deposit successful",
                $"Rs {amountPaise / 100.0:0} added to your wallet.", "Deposit");
            return new ApiResponse<object>(true, (string)pMsg.Value!, new { AmountRs = amountPaise / 100.0 });
        }
        return new ApiResponse<object>(false, (string)pMsg.Value!);
    }

    public async Task<ApiResponse<object>> DeductEntryFeeAsync(Guid playerId, Guid sessionId, long feePaise)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_DeductEntryFee", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        cmd.Parameters.AddWithValue("@FeePaise", feePaise);

        var pRes = cmd.Parameters.Add("@Result", SqlDbType.Int);
        pRes.Direction = ParameterDirection.Output;
        var pMsg = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        pMsg.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();

        var r = (int)pRes.Value!;
        return r == 1
            ? new ApiResponse<object>(true, (string)pMsg.Value!)
            : new ApiResponse<object>(false, (string)pMsg.Value!);
    }

    public async Task<ApiResponse<object>> WithdrawAsync(Guid playerId, WithdrawRequest req)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();

        await using (var stateCmd = new SqlCommand("SELECT StateCode FROM Players WHERE PlayerId = @PlayerId", cn))
        {
            stateCmd.Parameters.AddWithValue("@PlayerId", playerId);
            var stateCode = (await stateCmd.ExecuteScalarAsync())?.ToString();
            if (StateGeoblock.IsRestricted(stateCode))
                return new ApiResponse<object>(false, StateGeoblock.Message);
        }

        await using var cmd = new SqlCommand("USP_ProcessWithdrawal", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@AmountPaise", req.AmountPaise);

        var pRes = cmd.Parameters.Add("@Result", SqlDbType.Int);
        pRes.Direction = ParameterDirection.Output;
        var pMsg = cmd.Parameters.Add("@Message", SqlDbType.NVarChar, 200);
        pMsg.Direction = ParameterDirection.Output;
        await cmd.ExecuteNonQueryAsync();

        var r = (int)pRes.Value!;
        return r == 1
            ? new ApiResponse<object>(true, (string)pMsg.Value!, new { AmountRs = req.AmountPaise / 100.0 })
            : new ApiResponse<object>(false, (string)pMsg.Value!);
    }

    public async Task<object?> GetTransactionsAsync(Guid playerId, string? type, int page)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetTransactionHistory", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@TxnType", (object?)type ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@PageNo", page);
        cmd.Parameters.AddWithValue("@PageSize", 20);

        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new
            {
                TxnId = rdr["TxnId"].ToString(),
                TxnType = rdr["TxnType"].ToString(),
                AmountRs = (long)rdr["AmountPaise"] / 100.0,
                AmountPaise = (long)rdr["AmountPaise"],
                BalanceAfter = (long)rdr["BalanceAfter"],
                Status = rdr["Status"].ToString(),
                GatewayRef = rdr["GatewayRef"]?.ToString(),
                Remarks = rdr["Remarks"]?.ToString(),
                CreatedAt = (DateTime)rdr["CreatedAt"]
            });
        }

        return list;
    }

    public async Task<ApiResponse<object>> DevDepositAsync(Guid playerId, long amountPaise)
    {
        if (!_env.IsDevelopment())
            return new ApiResponse<object>(false, "Dev deposit only available in Development.");
        if (amountPaise < 5000)
            return new ApiResponse<object>(false, "Minimum deposit is Rs 50.");

        try
        {
            await using var cn = _db.CreateConnection();
            await cn.OpenAsync();
            await using var tx = (SqlTransaction)await cn.BeginTransactionAsync();

            await using (var check = new SqlCommand(
                "SELECT BalancePaise FROM Wallets WHERE PlayerId = @PlayerId", cn, tx))
            {
                check.Parameters.AddWithValue("@PlayerId", playerId);
                var exists = await check.ExecuteScalarAsync();
                if (exists == null || exists == DBNull.Value)
                {
                    await tx.RollbackAsync();
                    return new ApiResponse<object>(false, "Wallet not found. Please log out and register again.");
                }
            }

            await using (var upd = new SqlCommand(@"
                UPDATE Wallets SET BalancePaise = BalancePaise + @Amt, DepositPaise = DepositPaise + @Amt
                WHERE PlayerId = @PlayerId", cn, tx))
            {
                upd.Parameters.AddWithValue("@PlayerId", playerId);
                upd.Parameters.AddWithValue("@Amt", amountPaise);
                if (await upd.ExecuteNonQueryAsync() == 0)
                {
                    await tx.RollbackAsync();
                    return new ApiResponse<object>(false, "Wallet not found.");
                }
            }

            long newBal;
            await using (var balCmd = new SqlCommand(
                "SELECT BalancePaise FROM Wallets WHERE PlayerId = @PlayerId", cn, tx))
            {
                balCmd.Parameters.AddWithValue("@PlayerId", playerId);
                newBal = (long)(await balCmd.ExecuteScalarAsync() ?? 0L);
            }

            await using (var ins = new SqlCommand(@"
                INSERT INTO Transactions (TxnId, PlayerId, TxnType, AmountPaise, BalanceAfter, Status, GatewayRef, Remarks)
                VALUES (NEWID(), @PlayerId, 'Deposit', @Amt, @Bal, 'Success', @Ref, 'Dev test deposit')", cn, tx))
            {
                ins.Parameters.AddWithValue("@PlayerId", playerId);
                ins.Parameters.AddWithValue("@Amt", amountPaise);
                ins.Parameters.AddWithValue("@Bal", newBal);
                ins.Parameters.AddWithValue("@Ref", "DEV-" + Guid.NewGuid());
                await ins.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
            await _notifications.SendAsync(playerId, "Deposit successful",
                $"Rs {amountPaise / 100.0:0} added to your wallet (dev).", "Deposit");
            return new ApiResponse<object>(true, "Dev deposit credited", new { AmountRs = amountPaise / 100.0 });
        }
        catch (Exception ex)
        {
            return new ApiResponse<object>(false, $"Deposit failed: {ex.Message}");
        }
    }
}
