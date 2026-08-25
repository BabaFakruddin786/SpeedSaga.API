-- Free practice time modes (arrow was missing 4min/5min; other games had no free rows).

MERGE dbo.GamePlayTimeModes AS t
USING (VALUES
    ('arrow','free','4min','4 Min',240,4),
    ('arrow','free','5min','5 Min',300,5),
    ('car_parking','free','1min','1 Min',60,1),
    ('car_parking','free','2min','2 Min',120,2),
    ('car_parking','free','3min','3 Min',180,3),
    ('tic_tac_toe','free','1min','1 Min',60,1)
) AS s(GameType, PlayMode, TimeModeCode, DisplayLabel, BaseSeconds, SortOrder)
ON t.GameType = s.GameType AND t.PlayMode = s.PlayMode AND t.TimeModeCode = s.TimeModeCode
WHEN MATCHED THEN UPDATE SET DisplayLabel = s.DisplayLabel, BaseSeconds = s.BaseSeconds, SortOrder = s.SortOrder, IsActive = 1
WHEN NOT MATCHED THEN INSERT (GameType, PlayMode, TimeModeCode, DisplayLabel, BaseSeconds, SortOrder)
    VALUES (s.GameType, s.PlayMode, s.TimeModeCode, s.DisplayLabel, s.BaseSeconds, s.SortOrder);
GO
