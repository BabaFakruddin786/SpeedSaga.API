-- Dev utility: wipe all player/user activity so you can register fresh accounts.
-- Keeps: Levels, Tournaments (definitions), RestrictedStates.
USE SpeedSagaDB;
GO
SET QUOTED_IDENTIFIER ON;
GO

SET NOCOUNT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID('SessionMoves', 'U') IS NOT NULL DELETE FROM SessionMoves;
    IF OBJECT_ID('Replays', 'U') IS NOT NULL DELETE FROM Replays;
    IF OBJECT_ID('PlayerLevelHistory', 'U') IS NOT NULL DELETE FROM PlayerLevelHistory;
    IF OBJECT_ID('MatchmakingQueue', 'U') IS NOT NULL DELETE FROM MatchmakingQueue;
    IF OBJECT_ID('Transactions', 'U') IS NOT NULL DELETE FROM Transactions;
    IF OBJECT_ID('BotFlagLog', 'U') IS NOT NULL DELETE FROM BotFlagLog;
    IF OBJECT_ID('Notifications', 'U') IS NOT NULL DELETE FROM Notifications;
    IF OBJECT_ID('PlayerPromoClaims', 'U') IS NOT NULL DELETE FROM PlayerPromoClaims;
    IF OBJECT_ID('TournamentEntries', 'U') IS NOT NULL DELETE FROM TournamentEntries;
    IF OBJECT_ID('OtpSessions', 'U') IS NOT NULL DELETE FROM OtpSessions;
    IF OBJECT_ID('OutgoingMessages', 'U') IS NOT NULL DELETE FROM OutgoingMessages;
    IF OBJECT_ID('PasswordResetTokens', 'U') IS NOT NULL DELETE FROM PasswordResetTokens;
    IF OBJECT_ID('GameSessions', 'U') IS NOT NULL DELETE FROM GameSessions;

    IF OBJECT_ID('PlayerKYC', 'U') IS NOT NULL DELETE FROM PlayerKYC;
    IF OBJECT_ID('PlayerStats', 'U') IS NOT NULL DELETE FROM PlayerStats;
    IF OBJECT_ID('Wallets', 'U') IS NOT NULL DELETE FROM Wallets;

    IF OBJECT_ID('Players', 'U') IS NOT NULL
    BEGIN
        UPDATE Players SET ReferredBy = NULL;
        DELETE FROM Players;
    END

    COMMIT TRANSACTION;
    PRINT 'All player/user data cleared. Levels, tournaments, and config kept.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO
