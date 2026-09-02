USE SpeedSagaDB;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE dbo.USP_AdminSetPlayerBan
    @PlayerId       UNIQUEIDENTIFIER,
    @IsBanned       BIT,
    @BannedReason   NVARCHAR(500) = NULL,
    @Result         INT OUTPUT,
    @Message        NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;

    IF NOT EXISTS (SELECT 1 FROM dbo.Players WHERE PlayerId = @PlayerId)
    BEGIN
        SET @Result = -1;
        SET @Message = N'Player not found.';
        RETURN;
    END

    IF @IsBanned = 1 AND NULLIF(LTRIM(RTRIM(@BannedReason)), N'') IS NULL
    BEGIN
        SET @Result = -2;
        SET @Message = N'Ban reason is required.';
        RETURN;
    END

    UPDATE dbo.Players
    SET IsBanned = @IsBanned,
        BannedReason = CASE WHEN @IsBanned = 1 THEN NULLIF(LTRIM(RTRIM(@BannedReason)), N'') ELSE NULL END,
        IsActive = CASE WHEN @IsBanned = 1 THEN 0 ELSE 1 END
    WHERE PlayerId = @PlayerId;

    SET @Message = CASE WHEN @IsBanned = 1 THEN N'Player banned successfully.' ELSE N'Player unbanned successfully.' END;
END
GO

PRINT 'Updates_036_AdminPlayerBan applied.';
GO
