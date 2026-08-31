using System.Data;
using Microsoft.Data.SqlClient;
using SpeedSaga.API.Infrastructure;
using SpeedSaga.API.Models;

namespace SpeedSaga.API.Services;

public interface ISupportService
{
    Task<object?> GetConversationAsync(Guid playerId);
    Task<ApiResponse<object>> EscalateAsync(Guid playerId, string message);
    Task<ApiResponse<object>> SendPlayerMessageAsync(Guid playerId, string body);
    Task<object[]> ListTicketsAsync(string? status);
    Task<object?> GetTicketDetailAsync(Guid ticketId);
    Task<ApiResponse<object>> AdminReplyAsync(Guid ticketId, string body);
    Task<ApiResponse<object>> CloseTicketAsync(Guid ticketId);
}

public class SupportService : ISupportService
{
    readonly ISqlConnectionFactory _db;

    public SupportService(ISqlConnectionFactory db) => _db = db;

    public async Task<object?> GetConversationAsync(Guid playerId)
    {
        var ticket = await GetOpenTicketAsync(playerId);
        if (ticket == null)
            return new { hasTicket = false, ticketId = (string?)null, status = (string?)null, messages = Array.Empty<object>() };

        var messages = await GetMessagesAsync(ticket.TicketId);
        return new
        {
            hasTicket = true,
            ticketId = ticket.TicketId.ToString(),
            status = ticket.Status,
            subject = ticket.Subject,
            messages = messages.Select(m => new
            {
                messageId = m.MessageId.ToString(),
                senderType = m.SenderType,
                body = m.Body,
                createdAt = m.CreatedAt
            })
        };
    }

    public async Task<ApiResponse<object>> EscalateAsync(Guid playerId, string message)
    {
        message = message?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(message))
            return new ApiResponse<object>(false, "Describe your issue so our team can help");

        var existing = await GetOpenTicketAsync(playerId);
        if (existing != null)
        {
            await AddMessageAsync(existing.TicketId, "Player", message);
            return new ApiResponse<object>(true, "Message sent to support", new { ticketId = existing.TicketId.ToString() });
        }

        var subject = message.Length <= 120 ? message : message[..117] + "...";
        var ticketId = await CreateTicketAsync(playerId, subject, message);
        await AddMessageAsync(ticketId, "Bot",
            "Thanks — your message was sent to our support team. We usually reply within a few hours (Mon–Sat, 10 AM – 7 PM IST).");
        return new ApiResponse<object>(true, "Connected to support", new { ticketId = ticketId.ToString() });
    }

    public async Task<ApiResponse<object>> SendPlayerMessageAsync(Guid playerId, string body)
    {
        body = body?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(body))
            return new ApiResponse<object>(false, "Enter a message");

        var ticket = await GetOpenTicketAsync(playerId);
        if (ticket == null)
            return new ApiResponse<object>(false, "No active support conversation. Tap Chat with agent first.");

        await AddMessageAsync(ticket.TicketId, "Player", body);
        return new ApiResponse<object>(true, "Message sent");
    }

    public async Task<object[]> ListTicketsAsync(string? status)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_ListSupportTickets", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@Status", string.IsNullOrWhiteSpace(status) ? DBNull.Value : status.Trim());
        var list = new List<object>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new
            {
                ticketId = rdr["TicketId"].ToString(),
                playerId = rdr["PlayerId"].ToString(),
                username = rdr["Username"]?.ToString(),
                contactPhone = rdr["ContactPhone"]?.ToString(),
                contactEmail = rdr["ContactEmail"]?.ToString(),
                status = rdr["Status"].ToString(),
                subject = rdr["Subject"]?.ToString(),
                lastMessage = rdr["LastMessage"]?.ToString(),
                createdAt = rdr["CreatedAt"],
                updatedAt = rdr["UpdatedAt"]
            });
        }
        return list.ToArray();
    }

    public async Task<object?> GetTicketDetailAsync(Guid ticketId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand(@"
            SELECT T.TicketId, T.PlayerId, T.Status, T.Subject, T.CreatedAt, T.UpdatedAt,
                   P.Username, P.ContactEmail, P.ContactPhone
            FROM SupportTickets T
            INNER JOIN Players P ON P.PlayerId = T.PlayerId
            WHERE T.TicketId = @TicketId", cn);
        cmd.Parameters.AddWithValue("@TicketId", ticketId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return null;

        var detail = new
        {
            ticketId = rdr["TicketId"].ToString(),
            playerId = rdr["PlayerId"].ToString(),
            username = rdr["Username"]?.ToString(),
            contactEmail = rdr["ContactEmail"]?.ToString(),
            contactPhone = rdr["ContactPhone"]?.ToString(),
            status = rdr["Status"].ToString(),
            subject = rdr["Subject"]?.ToString(),
            createdAt = (DateTime)rdr["CreatedAt"],
            updatedAt = (DateTime)rdr["UpdatedAt"],
            messages = Array.Empty<object>()
        };
        await rdr.CloseAsync();

        var messages = await GetMessagesAsync(ticketId);
        return new
        {
            detail.ticketId,
            detail.playerId,
            detail.username,
            detail.contactEmail,
            detail.contactPhone,
            detail.status,
            detail.subject,
            detail.createdAt,
            detail.updatedAt,
            messages = messages.Select(m => new
            {
                messageId = m.MessageId.ToString(),
                senderType = m.SenderType,
                body = m.Body,
                createdAt = m.CreatedAt
            })
        };
    }

    public async Task<ApiResponse<object>> AdminReplyAsync(Guid ticketId, string body)
    {
        body = body?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(body))
            return new ApiResponse<object>(false, "Enter a reply message");
        if (!await TicketExistsAsync(ticketId))
            return new ApiResponse<object>(false, "Ticket not found");

        await AddMessageAsync(ticketId, "Agent", body);
        return new ApiResponse<object>(true, "Reply sent");
    }

    public async Task<ApiResponse<object>> CloseTicketAsync(Guid ticketId)
    {
        if (!await TicketExistsAsync(ticketId))
            return new ApiResponse<object>(false, "Ticket not found");

        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_CloseSupportTicket", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@TicketId", ticketId);
        await cmd.ExecuteNonQueryAsync();
        return new ApiResponse<object>(true, "Ticket closed");
    }

    async Task<TicketRow?> GetOpenTicketAsync(Guid playerId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetOpenSupportTicket", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        await using var rdr = await cmd.ExecuteReaderAsync();
        if (!await rdr.ReadAsync()) return null;
        return new TicketRow(
            rdr.GetGuid(rdr.GetOrdinal("TicketId")),
            rdr["Status"].ToString() ?? "AwaitingAgent",
            rdr["Subject"]?.ToString());
    }

    async Task<List<MessageRow>> GetMessagesAsync(Guid ticketId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_GetSupportMessages", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@TicketId", ticketId);
        var list = new List<MessageRow>();
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            list.Add(new MessageRow(
                rdr.GetGuid(rdr.GetOrdinal("MessageId")),
                rdr["SenderType"].ToString() ?? "Bot",
                rdr["Body"].ToString() ?? "",
                rdr.GetDateTime(rdr.GetOrdinal("CreatedAt"))));
        }
        return list;
    }

    async Task<Guid> CreateTicketAsync(Guid playerId, string subject, string initialBody)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_CreateSupportTicket", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@PlayerId", playerId);
        cmd.Parameters.AddWithValue("@Subject", subject);
        cmd.Parameters.AddWithValue("@InitialBody", initialBody);
        var outId = new SqlParameter("@TicketId", SqlDbType.UniqueIdentifier) { Direction = ParameterDirection.Output };
        cmd.Parameters.Add(outId);
        await cmd.ExecuteNonQueryAsync();
        return (Guid)outId.Value;
    }

    async Task AddMessageAsync(Guid ticketId, string senderType, string body)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("USP_AddSupportMessage", cn) { CommandType = CommandType.StoredProcedure };
        cmd.Parameters.AddWithValue("@TicketId", ticketId);
        cmd.Parameters.AddWithValue("@SenderType", senderType);
        cmd.Parameters.AddWithValue("@Body", body);
        await cmd.ExecuteNonQueryAsync();
    }

    async Task<bool> TicketExistsAsync(Guid ticketId)
    {
        await using var cn = _db.CreateConnection();
        await cn.OpenAsync();
        await using var cmd = new SqlCommand("SELECT 1 FROM SupportTickets WHERE TicketId = @TicketId", cn);
        cmd.Parameters.AddWithValue("@TicketId", ticketId);
        var result = await cmd.ExecuteScalarAsync();
        return result != null && result != DBNull.Value;
    }

    sealed record TicketRow(Guid TicketId, string Status, string? Subject);
    sealed record MessageRow(Guid MessageId, string SenderType, string Body, DateTime CreatedAt);
}
