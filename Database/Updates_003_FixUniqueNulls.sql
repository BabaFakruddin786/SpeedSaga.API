-- Fix: allow multiple players with NULL email OR NULL phone (email-only / phone-only signup)
SET QUOTED_IDENTIFIER ON;
GO

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_Player_Email')
    ALTER TABLE Players DROP CONSTRAINT UQ_Player_Email;

IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_Player_Phone')
    ALTER TABLE Players DROP CONSTRAINT UQ_Player_Phone;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_Player_Email_NotNull')
    CREATE UNIQUE INDEX UQ_Player_Email_NotNull ON Players(ContactEmail) WHERE ContactEmail IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_Player_Phone_NotNull')
    CREATE UNIQUE INDEX UQ_Player_Phone_NotNull ON Players(ContactPhone) WHERE ContactPhone IS NOT NULL;
GO

PRINT 'Unique null constraints fixed for Players table.';
GO

-- Stored procedures must be created with QUOTED_IDENTIFIER ON when filtered indexes exist.
SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE USP_RegisterPlayer
    @ContactEmail   NVARCHAR(150)   = NULL,
    @ContactPhone   NVARCHAR(15)    = NULL,
    @PasswordHash   NVARCHAR(512),
    @PasswordSalt   NVARCHAR(256),
    @ReferralCode   NVARCHAR(20)    = NULL,
    @StateCode      NVARCHAR(10)    = NULL,
    @NewPlayerId    UNIQUEIDENTIFIER OUTPUT,
    @Result         INT             OUTPUT,
    @Message        NVARCHAR(200)   OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    IF @StateCode IS NOT NULL AND EXISTS (SELECT 1 FROM RestrictedStates WHERE StateCode = @StateCode AND IsActive = 1)
    BEGIN
        SET @Result = -3; SET @Message = 'Real-money gaming is not available in your state.'; RETURN;
    END

    IF @ContactEmail IS NOT NULL AND EXISTS (SELECT 1 FROM Players WHERE ContactEmail = @ContactEmail)
    BEGIN
        SET @Result = -1; SET @Message = 'Email already registered.'; RETURN;
    END

    IF @ContactPhone IS NOT NULL AND EXISTS (SELECT 1 FROM Players WHERE ContactPhone = @ContactPhone)
    BEGIN
        SET @Result = -2; SET @Message = 'Phone number already registered.'; RETURN;
    END

    DECLARE @MyReferral NVARCHAR(20) = UPPER(LEFT(REPLACE(CAST(NEWID() AS NVARCHAR(36)),'-',''), 8));
    DECLARE @ReferrerId UNIQUEIDENTIFIER = NULL;
    IF @ReferralCode IS NOT NULL
        SELECT @ReferrerId = PlayerId FROM Players WHERE ReferralCode = @ReferralCode;

    BEGIN TRANSACTION;
    BEGIN TRY
        SET @NewPlayerId = NEWID();
        INSERT INTO Players (PlayerId, ContactEmail, ContactPhone, PasswordHash, PasswordSalt, ReferralCode, ReferredBy, StateCode)
        VALUES (@NewPlayerId, @ContactEmail, @ContactPhone, @PasswordHash, @PasswordSalt, @MyReferral, @ReferrerId, @StateCode);
        INSERT INTO Wallets (PlayerId) VALUES (@NewPlayerId);
        INSERT INTO PlayerStats (PlayerId) VALUES (@NewPlayerId);
        INSERT INTO PlayerKYC (PlayerId) VALUES (@NewPlayerId);
        IF @ReferrerId IS NOT NULL
            UPDATE Wallets SET BalancePaise += 10000, BonusPaise += 10000 WHERE PlayerId = @ReferrerId;
        COMMIT;
        SET @Result = 1; SET @Message = 'Registration successful.';
    END TRY
    BEGIN CATCH
        ROLLBACK;
        SET @Result = -99; SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO

PRINT 'USP_RegisterPlayer recreated with QUOTED_IDENTIFIER ON.';
GO

SET QUOTED_IDENTIFIER ON;
GO

CREATE OR ALTER PROCEDURE USP_LoginPlayer
    @Contact        NVARCHAR(150),
    @PlayerId       UNIQUEIDENTIFIER    OUTPUT,
    @PasswordHash   NVARCHAR(512)       OUTPUT,
    @PasswordSalt   NVARCHAR(256)       OUTPUT,
    @StateCode      NVARCHAR(10)        OUTPUT,
    @Result         INT                 OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT  @PlayerId       = PlayerId,
            @PasswordHash   = PasswordHash,
            @PasswordSalt   = PasswordSalt,
            @StateCode      = StateCode,
            @Result         = CASE WHEN IsBanned = 1 THEN -1 ELSE 1 END
    FROM Players
    WHERE (ContactEmail = @Contact OR ContactPhone = @Contact) AND IsActive = 1;

    IF @PlayerId IS NOT NULL
        UPDATE Players SET LastLoginAt = GETDATE() WHERE PlayerId = @PlayerId;
    ELSE
        SET @Result = 0;
END
GO

PRINT 'USP_LoginPlayer recreated with QUOTED_IDENTIFIER ON.';
