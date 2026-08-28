USE SpeedSagaDB;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'AppTickerConfig')
BEGIN
    CREATE TABLE dbo.AppTickerConfig (
        ConfigId            INT             NOT NULL CONSTRAINT PK_AppTickerConfig PRIMARY KEY,
        IsEnabled           BIT             NOT NULL CONSTRAINT DF_AppTickerConfig_IsEnabled DEFAULT 1,
        RotateSeconds       INT             NOT NULL CONSTRAINT DF_AppTickerConfig_RotateSeconds DEFAULT 3,
        MessagesJson        NVARCHAR(MAX)   NOT NULL,
        UpdatedAt           DATETIME2       NOT NULL CONSTRAINT DF_AppTickerConfig_UpdatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_AppTickerConfig_SingleRow CHECK (ConfigId = 1),
        CONSTRAINT CK_AppTickerConfig_RotateSeconds CHECK (RotateSeconds BETWEEN 2 AND 30)
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.AppTickerConfig WHERE ConfigId = 1)
BEGIN
    INSERT INTO dbo.AppTickerConfig (ConfigId, IsEnabled, RotateSeconds, MessagesJson)
    VALUES (1, 1, 3, N'[
        "Tournament starts in 2 hours! Join now",
        "New entry fee bracket Rs 750 added",
        "Complete 5 levels, earn bonus Rs 200"
    ]');
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_GetAppTickerConfig
AS
BEGIN
    SET NOCOUNT ON;
    SELECT IsEnabled, RotateSeconds, MessagesJson, UpdatedAt
    FROM dbo.AppTickerConfig
    WHERE ConfigId = 1;
END
GO

CREATE OR ALTER PROCEDURE dbo.USP_UpdateAppTickerConfig
    @IsEnabled       BIT,
    @RotateSeconds   INT,
    @MessagesJson    NVARCHAR(MAX),
    @Result          INT OUTPUT,
    @Message         NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;
    SET @Message = N'Ticker config updated.';

    IF @RotateSeconds < 2 OR @RotateSeconds > 30
    BEGIN
        SET @Result = 1;
        SET @Message = N'RotateSeconds must be between 2 and 30.';
        RETURN;
    END

    IF ISNULL(LTRIM(RTRIM(@MessagesJson)), N'') = N''
    BEGIN
        SET @Result = 1;
        SET @Message = N'At least one ticker message is required.';
        RETURN;
    END

    IF NOT EXISTS (SELECT 1 FROM dbo.AppTickerConfig WHERE ConfigId = 1)
    BEGIN
        INSERT INTO dbo.AppTickerConfig (ConfigId, IsEnabled, RotateSeconds, MessagesJson)
        VALUES (1, @IsEnabled, @RotateSeconds, @MessagesJson);
    END
    ELSE
    BEGIN
        UPDATE dbo.AppTickerConfig
        SET IsEnabled = @IsEnabled,
            RotateSeconds = @RotateSeconds,
            MessagesJson = @MessagesJson,
            UpdatedAt = SYSUTCDATETIME()
        WHERE ConfigId = 1;
    END
END
GO

PRINT 'Updates 031 applied: AppTickerConfig table and procedures.';
GO
