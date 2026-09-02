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
        SUM(CASE WHEN IsActive = 1 AND (ArrowCount < 30 OR GridCols <> 32) THEN 1 ELSE 0 END) AS LegacyActiveCount
    FROM dbo.Levels;
END
GO

PRINT 'Updates_038_AdminLevelSummaryDetail applied.';
GO
