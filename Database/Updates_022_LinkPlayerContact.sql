-- SpeedSaga Updates 022: Link email or phone to existing player (dual login).
USE SpeedSagaDB;
GO
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE USP_UpdatePlayerProfile
    @PlayerId       UNIQUEIDENTIFIER,
    @Username       NVARCHAR(50)    = NULL,
    @StateCode      NVARCHAR(10)    = NULL,
    @ContactEmail   NVARCHAR(150)   = NULL,
    @ContactPhone   NVARCHAR(15)    = NULL,
    @Result         INT             OUTPUT,
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;
    SET @Message = 'Profile updated.';

    SET @Username = NULLIF(LTRIM(RTRIM(@Username)), '');
    SET @StateCode = NULLIF(LTRIM(RTRIM(@StateCode)), '');
    SET @ContactEmail = NULLIF(LTRIM(RTRIM(@ContactEmail)), '');
    SET @ContactPhone = NULLIF(LTRIM(RTRIM(@ContactPhone)), '');

    IF NOT EXISTS (SELECT 1 FROM Players WHERE PlayerId = @PlayerId AND IsActive = 1)
    BEGIN SET @Result = -7; SET @Message = 'Player not found.'; RETURN; END

    DECLARE @CurEmail NVARCHAR(150), @CurPhone NVARCHAR(15);
    SELECT @CurEmail = ContactEmail, @CurPhone = ContactPhone
    FROM Players WHERE PlayerId = @PlayerId;

    IF @ContactEmail IS NOT NULL
    BEGIN
        IF @CurEmail IS NOT NULL AND @CurEmail <> @ContactEmail
        BEGIN SET @Result = -3; SET @Message = 'Email is already linked to this account.'; RETURN; END

        IF @CurEmail IS NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM Players WHERE ContactEmail = @ContactEmail AND PlayerId <> @PlayerId)
            BEGIN
                IF EXISTS (SELECT 1 FROM Players WHERE ContactEmail = @ContactEmail AND IsBanned = 1)
                BEGIN SET @Result = -4; SET @Message = 'This account is banned.'; RETURN; END
                SET @Result = -1; SET @Message = 'Email already registered to another account.'; RETURN;
            END
        END
    END

    IF @ContactPhone IS NOT NULL
    BEGIN
        IF @CurPhone IS NOT NULL AND @CurPhone <> @ContactPhone
        BEGIN SET @Result = -5; SET @Message = 'Mobile number is already linked to this account.'; RETURN; END

        IF @CurPhone IS NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM Players WHERE ContactPhone = @ContactPhone AND PlayerId <> @PlayerId)
            BEGIN
                IF EXISTS (SELECT 1 FROM Players WHERE ContactPhone = @ContactPhone AND IsBanned = 1)
                BEGIN SET @Result = -4; SET @Message = 'This account is banned.'; RETURN; END
                SET @Result = -2; SET @Message = 'Phone number already registered to another account.'; RETURN;
            END
        END
    END

    UPDATE Players SET
        Username = COALESCE(@Username, Username),
        StateCode = COALESCE(@StateCode, StateCode),
        ContactEmail = CASE WHEN @ContactEmail IS NOT NULL AND ContactEmail IS NULL THEN @ContactEmail ELSE ContactEmail END,
        ContactPhone = CASE WHEN @ContactPhone IS NOT NULL AND ContactPhone IS NULL THEN @ContactPhone ELSE ContactPhone END
    WHERE PlayerId = @PlayerId;

    SET @Result = 1;
    SET @Message = 'Profile updated.';
END
GO

PRINT 'Updates 022 applied: USP_UpdatePlayerProfile supports linking email/phone.';
GO
