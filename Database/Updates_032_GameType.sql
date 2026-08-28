USE SpeedSagaDB;
GO

-- Multi-game support: arrow, car_parking, tic_tac_toe on sessions and matchmaking queue.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('GameSessions') AND name = 'GameType')
BEGIN
    ALTER TABLE dbo.GameSessions
        ADD GameType NVARCHAR(30) NOT NULL
            CONSTRAINT DF_GameSessions_GameType DEFAULT 'arrow';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('MatchmakingQueue') AND name = 'GameType')
BEGIN
    ALTER TABLE dbo.MatchmakingQueue
        ADD GameType NVARCHAR(30) NOT NULL
            CONSTRAINT DF_MatchmakingQueue_GameType DEFAULT 'arrow';
END
GO

UPDATE dbo.GameSessions SET GameType = 'arrow' WHERE GameType IS NULL OR LTRIM(RTRIM(GameType)) = N'';
UPDATE dbo.MatchmakingQueue SET GameType = 'arrow' WHERE GameType IS NULL OR LTRIM(RTRIM(GameType)) = N'';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Queue_FeeGameType' AND object_id = OBJECT_ID('MatchmakingQueue'))
BEGIN
    CREATE NONCLUSTERED INDEX IX_Queue_FeeGameType
        ON dbo.MatchmakingQueue (EntryFeePaise, TimeLimitSecs, GameType, Status, JoinedAt);
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_MatchmakingJoin
    @PlayerId       UNIQUEIDENTIFIER,
    @FeePaise       BIGINT,
    @TimeSecs       INT,
    @ConnId         NVARCHAR(200),
    @GameType       NVARCHAR(30) = N'arrow',
    @SessionId      UNIQUEIDENTIFIER    OUTPUT,
    @IsNewSession   BIT                 OUTPUT,
    @OpponentId     UNIQUEIDENTIFIER    OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @IsNewSession = 0;
    SET @OpponentId = NULL;
    SET @SessionId = NULL;

    IF @GameType IS NULL OR LTRIM(RTRIM(@GameType)) = N''
        SET @GameType = N'arrow';

    BEGIN TRANSACTION;
    BEGIN TRY
        DECLARE @WaitId UNIQUEIDENTIFIER, @WaitPlayerId UNIQUEIDENTIFIER;

        SELECT TOP 1 @WaitId = QueueId, @WaitPlayerId = PlayerId
        FROM dbo.MatchmakingQueue WITH (UPDLOCK, ROWLOCK)
        WHERE EntryFeePaise = @FeePaise
          AND TimeLimitSecs = @TimeSecs
          AND GameType = @GameType
          AND Status = N'Waiting'
          AND PlayerId <> @PlayerId
          AND DATEDIFF(SECOND, JoinedAt, GETDATE()) < 60
        ORDER BY JoinedAt;

        IF @WaitPlayerId IS NOT NULL
        BEGIN
            SET @SessionId = NEWID();
            SET @OpponentId = @WaitPlayerId;

            INSERT INTO dbo.GameSessions (
                SessionId, Player1Id, Player2Id, GameMode, GameType,
                EntryFeePaise, RewardPaise, TimeLimitSecs, Status, StartedAt, SignalRGroupId)
            VALUES (
                @SessionId, @WaitPlayerId, @PlayerId, N'TwoPlayer', @GameType,
                @FeePaise, CAST(@FeePaise * 2 * 0.85 AS BIGINT), @TimeSecs,
                N'Active', GETDATE(), CAST(@SessionId AS NVARCHAR(50)));

            UPDATE dbo.MatchmakingQueue
            SET Status = N'Matched', MatchedAt = GETDATE(), MatchedSessionId = @SessionId
            WHERE QueueId = @WaitId;

            INSERT INTO dbo.MatchmakingQueue (
                PlayerId, EntryFeePaise, TimeLimitSecs, SignalRConnId, GameType, Status, MatchedAt, MatchedSessionId)
            VALUES (
                @PlayerId, @FeePaise, @TimeSecs, @ConnId, @GameType, N'Matched', GETDATE(), @SessionId);
        END
        ELSE
        BEGIN
            SET @IsNewSession = 1;
            INSERT INTO dbo.MatchmakingQueue (
                PlayerId, EntryFeePaise, TimeLimitSecs, SignalRConnId, GameType, Status)
            VALUES (
                @PlayerId, @FeePaise, @TimeSecs, @ConnId, @GameType, N'Waiting');
        END

        COMMIT;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK;
        THROW;
    END CATCH
END
GO

PRINT 'Updates 032 applied: GameType column + USP_MatchmakingJoin.';
GO
