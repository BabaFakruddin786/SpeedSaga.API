-- SpeedSaga migration 026: configurable starting lives per game type / play mode / tier
-- Run on the same database used by SpeedSaga.API
-- After running, extend GET /games/play-config to return startingLives + livesByTier

IF OBJECT_ID(N'dbo.GamePlayLives', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GamePlayLives (
        GameType    NVARCHAR(32)  NOT NULL,
        PlayMode    NVARCHAR(32)  NOT NULL,
        PuzzleTier  NVARCHAR(32)  NOT NULL CONSTRAINT DF_GamePlayLives_PuzzleTier DEFAULT N'',
        StartingLives INT NOT NULL CONSTRAINT DF_GamePlayLives_StartingLives DEFAULT 3,
        SortOrder   INT NOT NULL CONSTRAINT DF_GamePlayLives_SortOrder DEFAULT 0,
        CONSTRAINT PK_GamePlayLives PRIMARY KEY (GameType, PlayMode, PuzzleTier)
    );
END
GO

MERGE dbo.GamePlayLives AS t
USING (VALUES
    (N'arrow',        N'free',        N'',           3, 0),
    (N'arrow',        N'free',        N'Easy',       3, 1),
    (N'arrow',        N'free',        N'Medium',     3, 2),
    (N'arrow',        N'free',        N'Hard',       3, 3),
    (N'arrow',        N'free',        N'SuperHard',  3, 4),
    (N'arrow',        N'single',      N'',           3, 0),
    (N'arrow',        N'single',      N'Easy',       3, 1),
    (N'arrow',        N'single',      N'Medium',     3, 2),
    (N'arrow',        N'single',      N'Hard',       3, 3),
    (N'arrow',        N'single',      N'SuperHard',  3, 4),
    (N'arrow',        N'two_player',  N'',           3, 0),
    (N'car_parking',  N'free',        N'',           3, 0),
    (N'car_parking',  N'single',      N'',           3, 0),
    (N'tic_tac_toe',  N'free',        N'',           3, 0),
    (N'tic_tac_toe',  N'two_player',  N'',           3, 0)
) AS s (GameType, PlayMode, PuzzleTier, StartingLives, SortOrder)
ON t.GameType = s.GameType AND t.PlayMode = s.PlayMode AND t.PuzzleTier = s.PuzzleTier
WHEN MATCHED THEN UPDATE SET StartingLives = s.StartingLives, SortOrder = s.SortOrder
WHEN NOT MATCHED THEN INSERT (GameType, PlayMode, PuzzleTier, StartingLives, SortOrder)
VALUES (s.GameType, s.PlayMode, s.PuzzleTier, s.StartingLives, s.SortOrder);
GO

-- Example: give Hard puzzles only 2 lives (uncomment to test tier overrides)
-- UPDATE dbo.GamePlayLives SET StartingLives = 2 WHERE PuzzleTier = N'Hard';
