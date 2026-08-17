-- SpeedSaga Updates 009: Tiered hard puzzles (Easy/Medium/SuperHard) + deactivate simple procedural levels
USE SpeedSagaDB;
GO

-- Remove straight-line-only procedural puzzles from pool
UPDATE Levels SET IsActive = 0
WHERE LEN(ISNULL(GridJson, '')) < 280
   OR ArrowCount < 12
   OR (GridJson LIKE '%"dir":"R"%' AND GridJson NOT LIKE '%[%[%[%[%' AND ArrowCount < 10);
GO

-- Tier column for template assignment
IF COL_LENGTH('Levels', 'PuzzleTier') IS NULL
    ALTER TABLE Levels ADD PuzzleTier NVARCHAR(12) NOT NULL DEFAULT 'Medium';
GO

UPDATE Levels SET PuzzleTier = CASE
    WHEN DifficultyScore >= 88 THEN 'Hard'
    WHEN DifficultyScore >= 76 THEN 'Medium'
    ELSE 'Easy'
END WHERE IsActive = 1;
GO

CREATE OR ALTER PROCEDURE USP_AllocateLevel
    @PlayerId       UNIQUEIDENTIFIER,
    @TimeMode       NVARCHAR(10),
    @RewardMode     NVARCHAR(10),
    @EntryFeePaise  BIGINT = 0,
    @LevelId        INT     OUTPUT,
    @GridJson       NVARCHAR(MAX) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @LevelId = NULL; SET @GridJson = NULL;

    DECLARE @WinRate DECIMAL(5,2) = 0;
    SELECT @WinRate = ISNULL(WinRatePct, 0) FROM PlayerStats WITH (NOLOCK) WHERE PlayerId = @PlayerId;

    DECLARE @GamesInMode INT = 0;
    SELECT @GamesInMode = COUNT(*)
    FROM GameSessions WITH (NOLOCK)
    WHERE Player1Id = @PlayerId AND GameMode LIKE 'SinglePlayer%'
      AND RewardMode = @RewardMode AND Status IN ('Active', 'Complete');

    -- Tier curve: reference-style easy → medium → super hard
    DECLARE @Tier NVARCHAR(12), @MinArrows INT, @MinDiff INT, @MaxDiff INT;
    IF @GamesInMode < 3
    BEGIN
        SET @Tier = 'Easy';      SET @MinArrows = 14; SET @MinDiff = 65; SET @MaxDiff = 78;
    END
    ELSE IF @GamesInMode < 8
    BEGIN
        SET @Tier = 'Medium';    SET @MinArrows = 18; SET @MinDiff = 76; SET @MaxDiff = 88;
    END
    ELSE
    BEGIN
        SET @Tier = 'Hard';      SET @MinArrows = 24; SET @MinDiff = 86; SET @MaxDiff = 100;
    END

    IF @RewardMode = '5x'
    BEGIN
        SET @Tier = CASE WHEN @GamesInMode < 2 THEN 'Medium' ELSE 'Hard' END;
        SET @MinArrows = CASE @Tier WHEN 'Medium' THEN 20 ELSE 26 END;
        SET @MinDiff = CASE @Tier WHEN 'Medium' THEN 80 ELSE 90 END;
        SET @MaxDiff = 100;
    END

    IF @EntryFeePaise >= 100000 BEGIN SET @MinArrows += 2; SET @MinDiff += 3; END
    IF @EntryFeePaise >= 200000 BEGIN SET @MinArrows += 2; SET @MinDiff += 4; END
    IF @WinRate > 25 AND @MinDiff < 88 SET @MinDiff = 88;

    DECLARE @ExcludeCount INT = 50;
    WHILE @ExcludeCount >= 3 AND @LevelId IS NULL
    BEGIN
        SELECT TOP 1 @LevelId = l.LevelId, @GridJson = l.GridJson
        FROM Levels l WITH (NOLOCK)
        WHERE l.TimeMode = @TimeMode AND l.IsActive = 1
          AND l.PuzzleTier = @Tier
          AND l.ArrowCount >= @MinArrows
          AND l.DifficultyScore BETWEEN @MinDiff AND @MaxDiff
          AND NOT EXISTS (
              SELECT 1 FROM (
                  SELECT TOP (@ExcludeCount) h.LevelId AS Lid
                  FROM PlayerLevelHistory h WITH (NOLOCK)
                  WHERE h.PlayerId = @PlayerId ORDER BY h.PlayedAt DESC
              ) recent WHERE recent.Lid = l.LevelId
          )
        ORDER BY l.ArrowCount DESC, NEWID();

        IF @LevelId IS NULL
            SELECT TOP 1 @LevelId = l.LevelId, @GridJson = l.GridJson
            FROM Levels l WITH (NOLOCK)
            WHERE l.TimeMode = @TimeMode AND l.IsActive = 1
              AND l.ArrowCount >= @MinArrows
              AND l.DifficultyScore BETWEEN @MinDiff AND @MaxDiff
              AND NOT EXISTS (
                  SELECT 1 FROM (
                      SELECT TOP (@ExcludeCount) h.LevelId AS Lid
                      FROM PlayerLevelHistory h WITH (NOLOCK)
                      WHERE h.PlayerId = @PlayerId ORDER BY h.PlayedAt DESC
              ) recent WHERE recent.Lid = l.LevelId)
            ORDER BY l.ArrowCount DESC, NEWID();

        SET @ExcludeCount = CASE @ExcludeCount WHEN 50 THEN 10 WHEN 10 THEN 3 ELSE 0 END;
    END

    IF @LevelId IS NULL
        SELECT TOP 1 @LevelId = l.LevelId, @GridJson = l.GridJson
        FROM Levels l WITH (NOLOCK)
        WHERE l.TimeMode = @TimeMode AND l.IsActive = 1 AND l.ArrowCount >= 12
        ORDER BY l.ArrowCount DESC, NEWID();
END
GO

PRINT 'Updates_009_ComplexPuzzleTiers applied.';
GO
