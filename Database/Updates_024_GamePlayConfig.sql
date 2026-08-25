-- Remote-configurable reward modes, time limits, and entry fees per game/play mode.
-- Set IsActive = 0 to hide an option in the app without redeploying.

IF OBJECT_ID('dbo.GamePlayRewardModes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GamePlayRewardModes (
        RewardModeId     INT IDENTITY(1,1) PRIMARY KEY,
        GameType         NVARCHAR(30)  NOT NULL DEFAULT 'arrow',
        PlayMode         NVARCHAR(20)  NOT NULL DEFAULT 'single',  -- single | two_player | free
        RewardModeCode   NVARCHAR(10)  NOT NULL,
        DisplayName      NVARCHAR(60)  NOT NULL,
        HintText         NVARCHAR(200) NULL,
        RewardMultiplier DECIMAL(6,2)  NOT NULL,
        TimeLimitFactor  DECIMAL(6,3)  NOT NULL,
        SortOrder        INT           NOT NULL DEFAULT 0,
        IsActive         BIT           NOT NULL DEFAULT 1,
        CONSTRAINT UQ_GamePlayRewardModes UNIQUE (GameType, PlayMode, RewardModeCode)
    );
END
GO

IF OBJECT_ID('dbo.GamePlayTimeModes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GamePlayTimeModes (
        TimeModeId       INT IDENTITY(1,1) PRIMARY KEY,
        GameType         NVARCHAR(30)  NOT NULL DEFAULT 'arrow',
        PlayMode         NVARCHAR(20)  NOT NULL DEFAULT 'single',
        TimeModeCode     NVARCHAR(20)  NOT NULL,
        DisplayLabel     NVARCHAR(40)  NOT NULL,
        BaseSeconds      INT           NOT NULL,
        SortOrder        INT           NOT NULL DEFAULT 0,
        IsActive         BIT           NOT NULL DEFAULT 1,
        CONSTRAINT UQ_GamePlayTimeModes UNIQUE (GameType, PlayMode, TimeModeCode)
    );
END
GO

IF OBJECT_ID('dbo.GamePlayEntryFees', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GamePlayEntryFees (
        EntryFeeId       INT IDENTITY(1,1) PRIMARY KEY,
        GameType         NVARCHAR(30)  NOT NULL DEFAULT 'arrow',
        PlayMode         NVARCHAR(20)  NOT NULL DEFAULT 'single',
        EntryFeePaise    BIGINT        NOT NULL,
        SortOrder        INT           NOT NULL DEFAULT 0,
        IsActive         BIT           NOT NULL DEFAULT 1,
        CONSTRAINT UQ_GamePlayEntryFees UNIQUE (GameType, PlayMode, EntryFeePaise)
    );
END
GO

IF OBJECT_ID('dbo.GamePlaySettings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GamePlaySettings (
        SettingKey   NVARCHAR(60)  NOT NULL PRIMARY KEY,
        SettingValue NVARCHAR(200) NOT NULL,
        UpdatedAt    DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- Reward modes (arrow single-player): 1x + existing 3x / 5x
MERGE dbo.GamePlayRewardModes AS t
USING (VALUES
    ('arrow','single','1x','1x Rewards','Standard time · 1× entry reward',1.00,1.000,1),
    ('arrow','single','3x','3x Rewards','3× = more time to solve · lower reward multiplier',3.00,0.750,2),
    ('arrow','single','5x','5x Rewards','5× = higher reward · shorter time limit',5.00,0.500,3),
    ('car_parking','single','3x','3x Rewards','More time to solve the lot',3.00,0.750,1),
    ('car_parking','single','5x','5x Rewards','Higher reward · shorter time',5.00,0.500,2)
) AS s(GameType, PlayMode, RewardModeCode, DisplayName, HintText, RewardMultiplier, TimeLimitFactor, SortOrder)
ON t.GameType = s.GameType AND t.PlayMode = s.PlayMode AND t.RewardModeCode = s.RewardModeCode
WHEN MATCHED THEN UPDATE SET
    DisplayName = s.DisplayName, HintText = s.HintText,
    RewardMultiplier = s.RewardMultiplier, TimeLimitFactor = s.TimeLimitFactor,
    SortOrder = s.SortOrder, IsActive = 1
WHEN NOT MATCHED THEN INSERT (GameType, PlayMode, RewardModeCode, DisplayName, HintText, RewardMultiplier, TimeLimitFactor, SortOrder)
    VALUES (s.GameType, s.PlayMode, s.RewardModeCode, s.DisplayName, s.HintText, s.RewardMultiplier, s.TimeLimitFactor, s.SortOrder);
GO

-- Time modes
MERGE dbo.GamePlayTimeModes AS t
USING (VALUES
    ('arrow','single','1min','1 Min',60,1),
    ('arrow','single','2min','2 Min',120,2),
    ('arrow','single','3min','3 Min',180,3),
    ('arrow','single','4min','4 Min',240,4),
    ('arrow','single','5min','5 Min',300,5),
    ('arrow','two_player','1min','1 Min',60,1),
    ('arrow','two_player','2min','2 Min',120,2),
    ('arrow','two_player','3min','3 Min',180,3),
    ('arrow','two_player','4min','4 Min',240,4),
    ('arrow','two_player','5min','5 Min',300,5),
    ('arrow','free','1min','1 Min',60,1),
    ('arrow','free','2min','2 Min',120,2),
    ('arrow','free','3min','3 Min',180,3),
    ('car_parking','single','1min','1 Min',60,1),
    ('car_parking','single','2min','2 Min',120,2),
    ('car_parking','single','3min','3 Min',180,3),
    ('tic_tac_toe','two_player','1min','1 Min',60,1),
    ('tic_tac_toe','two_player','2min','2 Min',120,2),
    ('tic_tac_toe','two_player','3min','3 Min',180,3)
) AS s(GameType, PlayMode, TimeModeCode, DisplayLabel, BaseSeconds, SortOrder)
ON t.GameType = s.GameType AND t.PlayMode = s.PlayMode AND t.TimeModeCode = s.TimeModeCode
WHEN MATCHED THEN UPDATE SET DisplayLabel = s.DisplayLabel, BaseSeconds = s.BaseSeconds, SortOrder = s.SortOrder, IsActive = 1
WHEN NOT MATCHED THEN INSERT (GameType, PlayMode, TimeModeCode, DisplayLabel, BaseSeconds, SortOrder)
    VALUES (s.GameType, s.PlayMode, s.TimeModeCode, s.DisplayLabel, s.BaseSeconds, s.SortOrder);
GO

-- Entry fees (paise)
DECLARE @Fees TABLE (GameType NVARCHAR(30), PlayMode NVARCHAR(20), EntryFeePaise BIGINT, SortOrder INT);
INSERT INTO @Fees VALUES
    ('arrow','single',5000,1),('arrow','single',10000,2),('arrow','single',20000,3),('arrow','single',30000,4),
    ('arrow','single',40000,5),('arrow','single',50000,6),('arrow','single',75000,7),('arrow','single',100000,8),
    ('arrow','single',150000,9),('arrow','single',200000,10),('arrow','single',250000,11),('arrow','single',300000,12),
    ('arrow','single',400000,13),('arrow','single',500000,14),('arrow','single',750000,15),('arrow','single',1000000,16),
    ('arrow','two_player',5000,1),('arrow','two_player',10000,2),('arrow','two_player',20000,3),('arrow','two_player',30000,4),
    ('arrow','two_player',40000,5),('arrow','two_player',50000,6),('arrow','two_player',75000,7),('arrow','two_player',100000,8),
    ('arrow','two_player',150000,9),('arrow','two_player',200000,10),('arrow','two_player',250000,11),('arrow','two_player',300000,12),
    ('arrow','two_player',400000,13),('arrow','two_player',500000,14),('arrow','two_player',750000,15),('arrow','two_player',1000000,16),
    ('car_parking','single',5000,1),('car_parking','single',10000,2),('car_parking','single',20000,3),('car_parking','single',30000,4),
    ('car_parking','single',40000,5),('car_parking','single',50000,6),('car_parking','single',75000,7),('car_parking','single',100000,8),
    ('tic_tac_toe','two_player',5000,1),('tic_tac_toe','two_player',10000,2),('tic_tac_toe','two_player',20000,3),('tic_tac_toe','two_player',30000,4),
    ('tic_tac_toe','two_player',40000,5),('tic_tac_toe','two_player',50000,6),('tic_tac_toe','two_player',75000,7),('tic_tac_toe','two_player',100000,8);

MERGE dbo.GamePlayEntryFees AS t
USING @Fees AS s
ON t.GameType = s.GameType AND t.PlayMode = s.PlayMode AND t.EntryFeePaise = s.EntryFeePaise
WHEN MATCHED THEN UPDATE SET SortOrder = s.SortOrder, IsActive = 1
WHEN NOT MATCHED THEN INSERT (GameType, PlayMode, EntryFeePaise, SortOrder)
    VALUES (s.GameType, s.PlayMode, s.EntryFeePaise, s.SortOrder);
GO

MERGE dbo.GamePlaySettings AS t
USING (VALUES ('two_player_pool_percent','85')) AS s(SettingKey, SettingValue)
ON t.SettingKey = s.SettingKey
WHEN MATCHED THEN UPDATE SET SettingValue = s.SettingValue, UpdatedAt = SYSUTCDATETIME()
WHEN NOT MATCHED THEN INSERT (SettingKey, SettingValue) VALUES (s.SettingKey, s.SettingValue);
GO
