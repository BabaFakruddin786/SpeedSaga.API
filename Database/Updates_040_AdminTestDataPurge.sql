-- Temporary testing utility: delete player accounts and all related gameplay/finance data.
-- Keep disabled in production after go-live (Admin__AllowTestDataPurge=false).

USE SpeedSagaDB;
GO
SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminGetTestDataCounts
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        (SELECT COUNT(*) FROM dbo.Players) AS PlayerCount,
        (SELECT COUNT(*) FROM dbo.Transactions) AS TransactionCount,
        (SELECT COUNT(*) FROM dbo.GameSessions) AS GameSessionCount,
        (SELECT COUNT(*) FROM dbo.SupportTickets) AS SupportTicketCount,
        (SELECT COUNT(*) FROM dbo.Notifications) AS NotificationCount,
        (SELECT COUNT(*) FROM dbo.TournamentEntries) AS TournamentEntryCount;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminDeletePlayer
    @PlayerId UNIQUEIDENTIFIER,
    @Result   INT OUTPUT,
    @Message  NVARCHAR(300) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;
    SET @Message = N'';

    IF NOT EXISTS (SELECT 1 FROM dbo.Players WHERE PlayerId = @PlayerId)
    BEGIN
        SET @Result = -1;
        SET @Message = N'Player not found.';
        RETURN;
    END

    DECLARE @Sessions TABLE (SessionId UNIQUEIDENTIFIER PRIMARY KEY);
    INSERT INTO @Sessions (SessionId)
    SELECT SessionId FROM dbo.GameSessions WHERE Player1Id = @PlayerId OR Player2Id = @PlayerId;

    DELETE sm
    FROM dbo.SupportMessages sm
    INNER JOIN dbo.SupportTickets t ON t.TicketId = sm.TicketId
    WHERE t.PlayerId = @PlayerId;

    DELETE FROM dbo.SupportTickets WHERE PlayerId = @PlayerId;
    DELETE FROM dbo.SessionMoves WHERE SessionId IN (SELECT SessionId FROM @Sessions);
    DELETE FROM dbo.Replays WHERE SessionId IN (SELECT SessionId FROM @Sessions);
    DELETE FROM dbo.Transactions WHERE SessionId IN (SELECT SessionId FROM @Sessions);
    DELETE FROM dbo.GameSessions WHERE SessionId IN (SELECT SessionId FROM @Sessions);

    DELETE FROM dbo.SessionMoves WHERE PlayerId = @PlayerId;
    DELETE FROM dbo.Replays WHERE PlayerId = @PlayerId;
    DELETE FROM dbo.PlayerLevelHistory WHERE PlayerId = @PlayerId;
    DELETE FROM dbo.BotFlagLog WHERE PlayerId = @PlayerId;
    DELETE FROM dbo.Transactions WHERE PlayerId = @PlayerId;
    DELETE FROM dbo.Notifications WHERE PlayerId = @PlayerId;

    IF OBJECT_ID(N'dbo.PlayerDeviceTokens', N'U') IS NOT NULL
        DELETE FROM dbo.PlayerDeviceTokens WHERE PlayerId = @PlayerId;

    IF OBJECT_ID(N'dbo.PlayerPromoClaims', N'U') IS NOT NULL
        DELETE FROM dbo.PlayerPromoClaims WHERE PlayerId = @PlayerId;

    IF OBJECT_ID(N'dbo.TournamentEntries', N'U') IS NOT NULL
        DELETE FROM dbo.TournamentEntries WHERE PlayerId = @PlayerId;

    IF OBJECT_ID(N'dbo.MatchmakingQueue', N'U') IS NOT NULL
        DELETE FROM dbo.MatchmakingQueue WHERE PlayerId = @PlayerId;

    IF OBJECT_ID(N'dbo.PasswordResetTokens', N'U') IS NOT NULL
        DELETE FROM dbo.PasswordResetTokens WHERE PlayerId = @PlayerId;

    IF OBJECT_ID(N'dbo.OtpSessions', N'U') IS NOT NULL
        DELETE FROM dbo.OtpSessions WHERE PlayerId = @PlayerId;

    IF OBJECT_ID(N'dbo.OutgoingMessages', N'U') IS NOT NULL
        DELETE FROM dbo.OutgoingMessages WHERE PlayerId = @PlayerId;

    UPDATE dbo.Players SET ReferredBy = NULL WHERE ReferredBy = @PlayerId;

    DELETE FROM dbo.Wallets WHERE PlayerId = @PlayerId;
    DELETE FROM dbo.PlayerStats WHERE PlayerId = @PlayerId;
    DELETE FROM dbo.PlayerKYC WHERE PlayerId = @PlayerId;
    DELETE FROM dbo.Players WHERE PlayerId = @PlayerId;

    SET @Message = N'Player and all related data deleted.';
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminPurgeAllPlayerData
    @Result  INT OUTPUT,
    @Message NVARCHAR(300) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;
    SET @Message = N'';

    BEGIN TRY
        BEGIN TRAN;

        IF OBJECT_ID(N'dbo.SupportMessages', N'U') IS NOT NULL
            DELETE FROM dbo.SupportMessages;
        IF OBJECT_ID(N'dbo.SupportTickets', N'U') IS NOT NULL
            DELETE FROM dbo.SupportTickets;
        IF OBJECT_ID(N'dbo.SessionMoves', N'U') IS NOT NULL
            DELETE FROM dbo.SessionMoves;
        IF OBJECT_ID(N'dbo.Replays', N'U') IS NOT NULL
            DELETE FROM dbo.Replays;
        IF OBJECT_ID(N'dbo.PlayerLevelHistory', N'U') IS NOT NULL
            DELETE FROM dbo.PlayerLevelHistory;
        IF OBJECT_ID(N'dbo.BotFlagLog', N'U') IS NOT NULL
            DELETE FROM dbo.BotFlagLog;
        IF OBJECT_ID(N'dbo.Transactions', N'U') IS NOT NULL
            DELETE FROM dbo.Transactions;
        IF OBJECT_ID(N'dbo.Notifications', N'U') IS NOT NULL
            DELETE FROM dbo.Notifications;
        IF OBJECT_ID(N'dbo.PlayerDeviceTokens', N'U') IS NOT NULL
            DELETE FROM dbo.PlayerDeviceTokens;
        IF OBJECT_ID(N'dbo.PlayerPromoClaims', N'U') IS NOT NULL
            DELETE FROM dbo.PlayerPromoClaims;
        IF OBJECT_ID(N'dbo.TournamentEntries', N'U') IS NOT NULL
            DELETE FROM dbo.TournamentEntries;
        IF OBJECT_ID(N'dbo.MatchmakingQueue', N'U') IS NOT NULL
            DELETE FROM dbo.MatchmakingQueue;
        IF OBJECT_ID(N'dbo.GameSessions', N'U') IS NOT NULL
            DELETE FROM dbo.GameSessions;
        IF OBJECT_ID(N'dbo.PasswordResetTokens', N'U') IS NOT NULL
            DELETE FROM dbo.PasswordResetTokens;
        IF OBJECT_ID(N'dbo.OtpSessions', N'U') IS NOT NULL
            DELETE FROM dbo.OtpSessions;
        IF OBJECT_ID(N'dbo.OutgoingMessages', N'U') IS NOT NULL
            DELETE FROM dbo.OutgoingMessages;

        UPDATE dbo.Players SET ReferredBy = NULL WHERE ReferredBy IS NOT NULL;

        DELETE FROM dbo.Wallets;
        DELETE FROM dbo.PlayerStats;
        DELETE FROM dbo.PlayerKYC;
        DELETE FROM dbo.Players;

        COMMIT TRAN;
        SET @Message = N'All player accounts and related gameplay/finance data were deleted.';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRAN;
        SET @Result = -1;
        SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO

PRINT 'Updates 040 applied: Admin test data purge procedures.';
GO
