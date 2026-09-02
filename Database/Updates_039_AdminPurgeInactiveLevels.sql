USE SpeedSagaDB;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminGetLevelSummary
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        COUNT(*) AS TotalLevels,
        SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS ActiveLevels,
        SUM(CASE WHEN IsActive = 0 THEN 1 ELSE 0 END) AS InactiveLevels
    FROM dbo.Levels;

    SELECT TimeMode, COUNT(*) AS LevelCount,
           SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS ActiveCount
    FROM dbo.Levels
    GROUP BY TimeMode
    ORDER BY TimeMode;

    SELECT PuzzleTier, COUNT(*) AS LevelCount,
           SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS ActiveCount
    FROM dbo.Levels
    GROUP BY PuzzleTier
    ORDER BY PuzzleTier;

    SELECT
        MIN(LevelId) AS MinLevelId,
        MAX(LevelId) AS MaxLevelId,
        SUM(CASE WHEN IsActive = 0 AND (ArrowCount < 12 OR GridCols < 32 OR LEN(ISNULL(GridJson, '')) < 280) THEN 1 ELSE 0 END) AS LegacyInactiveCount,
        SUM(CASE WHEN IsActive = 1 AND ArrowCount >= 30 AND GridCols = 32 THEN 1 ELSE 0 END) AS ModernActiveCount,
        SUM(CASE WHEN IsActive = 0 AND NOT (ArrowCount < 12 OR GridCols < 32 OR LEN(ISNULL(GridJson, '')) < 280) THEN 1 ELSE 0 END) AS OtherInactiveCount,
        SUM(CASE WHEN IsActive = 1 AND (ArrowCount < 30 OR GridCols <> 32) THEN 1 ELSE 0 END) AS LegacyActiveCount,
        (SELECT COUNT(*)
         FROM dbo.Levels l
         WHERE l.IsActive = 0
           AND (l.ArrowCount < 12 OR l.GridCols < 32 OR LEN(ISNULL(l.GridJson, '')) < 280)
           AND NOT EXISTS (SELECT 1 FROM dbo.PlayerLevelHistory h WHERE h.LevelId = l.LevelId)
           AND NOT EXISTS (SELECT 1 FROM dbo.GameSessions g WHERE g.LevelId = l.LevelId)) AS PurgeableLegacyCount,
        (SELECT COUNT(*)
         FROM dbo.Levels l
         WHERE l.IsActive = 0
           AND (l.ArrowCount < 12 OR l.GridCols < 32 OR LEN(ISNULL(l.GridJson, '')) < 280)
           AND (
                EXISTS (SELECT 1 FROM dbo.PlayerLevelHistory h WHERE h.LevelId = l.LevelId)
             OR EXISTS (SELECT 1 FROM dbo.GameSessions g WHERE g.LevelId = l.LevelId)
           )) AS PurgeBlockedLegacyCount
    FROM dbo.Levels;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminPurgeInactiveLevels
    @LegacyOnly     BIT = 1,
    @Result         INT OUTPUT,
    @Message        NVARCHAR(500) OUTPUT,
    @Deleted        INT OUTPUT,
    @Skipped        INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;
    SET @Deleted = 0;
    SET @Skipped = 0;
    SET @Message = N'';

    SELECT @Skipped = COUNT(*)
    FROM dbo.Levels l
    WHERE l.IsActive = 0
      AND (@LegacyOnly = 0 OR (l.ArrowCount < 12 OR l.GridCols < 32 OR LEN(ISNULL(l.GridJson, '')) < 280))
      AND (
            EXISTS (SELECT 1 FROM dbo.PlayerLevelHistory h WHERE h.LevelId = l.LevelId)
         OR EXISTS (SELECT 1 FROM dbo.GameSessions g WHERE g.LevelId = l.LevelId)
      );

    DELETE l
    FROM dbo.Levels l
    WHERE l.IsActive = 0
      AND (@LegacyOnly = 0 OR (l.ArrowCount < 12 OR l.GridCols < 32 OR LEN(ISNULL(l.GridJson, '')) < 280))
      AND NOT EXISTS (SELECT 1 FROM dbo.PlayerLevelHistory h WHERE h.LevelId = l.LevelId)
      AND NOT EXISTS (SELECT 1 FROM dbo.GameSessions g WHERE g.LevelId = l.LevelId);

    SET @Deleted = @@ROWCOUNT;

    IF @Deleted = 0 AND @Skipped = 0
    BEGIN
        SET @Message = N'No inactive legacy levels matched the purge criteria.';
        RETURN;
    END

    SET @Message = N'Deleted ' + CAST(@Deleted AS NVARCHAR(10)) + N' unused inactive level slot(s).'
        + CASE WHEN @Skipped > 0 THEN N' Kept ' + CAST(@Skipped AS NVARCHAR(10)) + N' linked to past game history.' ELSE N'' END;
END
GO

PRINT 'Updates_039_AdminPurgeInactiveLevels applied.';
GO
