USE SpeedSagaDB;
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'PasswordResetTokens')
BEGIN
    CREATE TABLE PasswordResetTokens (
        TokenId     UNIQUEIDENTIFIER DEFAULT NEWID() PRIMARY KEY,
        PlayerId    UNIQUEIDENTIFIER NOT NULL REFERENCES Players(PlayerId),
        CodeHash    NVARCHAR(256)    NOT NULL,
        ExpiresAt   DATETIME         NOT NULL,
        IsUsed      BIT              DEFAULT 0,
        CreatedAt   DATETIME         DEFAULT GETDATE()
    );
    CREATE INDEX IX_Reset_Player ON PasswordResetTokens(PlayerId, CreatedAt DESC);
END
GO

CREATE OR ALTER PROCEDURE USP_ForgotPassword
    @Contact    NVARCHAR(150),
    @CodeHash   NVARCHAR(256),
    @ExpiresAt  DATETIME,
    @Result     INT OUTPUT,
    @Message    NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @PlayerId UNIQUEIDENTIFIER;
    SELECT @PlayerId = PlayerId FROM Players
    WHERE (ContactEmail = @Contact OR ContactPhone = @Contact) AND IsActive = 1;

    IF @PlayerId IS NULL
    BEGIN SET @Result = 1; SET @Message = 'If the account exists, a reset code has been sent.'; RETURN; END

    INSERT INTO PasswordResetTokens (PlayerId, CodeHash, ExpiresAt)
    VALUES (@PlayerId, @CodeHash, @ExpiresAt);

    SET @Result = 1;
    SET @Message = 'If the account exists, a reset code has been sent.';
END
GO

CREATE OR ALTER PROCEDURE USP_ResetPassword
    @Contact        NVARCHAR(150),
    @CodeHash       NVARCHAR(256),
    @NewPasswordHash NVARCHAR(512),
    @NewPasswordSalt NVARCHAR(256),
    @Result         INT OUTPUT,
    @Message        NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @PlayerId UNIQUEIDENTIFIER;
    SELECT @PlayerId = PlayerId FROM Players
    WHERE (ContactEmail = @Contact OR ContactPhone = @Contact) AND IsActive = 1;

    IF @PlayerId IS NULL BEGIN SET @Result = -1; SET @Message = 'Invalid reset request.'; RETURN; END

    IF NOT EXISTS (
        SELECT 1 FROM PasswordResetTokens
        WHERE PlayerId = @PlayerId AND CodeHash = @CodeHash AND IsUsed = 0 AND ExpiresAt > GETDATE()
    )
    BEGIN SET @Result = -2; SET @Message = 'Invalid or expired reset code.'; RETURN; END

    UPDATE Players SET PasswordHash = @NewPasswordHash, PasswordSalt = @NewPasswordSalt WHERE PlayerId = @PlayerId;
    UPDATE PasswordResetTokens SET IsUsed = 1 WHERE PlayerId = @PlayerId AND CodeHash = @CodeHash AND IsUsed = 0;

    SET @Result = 1; SET @Message = 'Password reset successful.';
END
GO

CREATE OR ALTER PROCEDURE USP_ProcessWithdrawal
    @PlayerId       UNIQUEIDENTIFIER,
    @AmountPaise    BIGINT,
    @Result         INT OUTPUT,
    @Message        NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @KycVerified BIT = 0, @Balance BIGINT = 0;

    SELECT @KycVerified = ISNULL(K.IsFullyVerified, 0), @Balance = W.BalancePaise
    FROM Wallets W
    LEFT JOIN PlayerKYC K ON K.PlayerId = W.PlayerId
    WHERE W.PlayerId = @PlayerId;

    IF @KycVerified = 0 BEGIN SET @Result = -1; SET @Message = 'Complete KYC before withdrawal.'; RETURN; END
    IF @AmountPaise < 10000 BEGIN SET @Result = -2; SET @Message = 'Minimum withdrawal is Rs 100.'; RETURN; END
    IF @Balance < @AmountPaise BEGIN SET @Result = -3; SET @Message = 'Insufficient balance.'; RETURN; END

    BEGIN TRANSACTION;
    BEGIN TRY
        UPDATE Wallets
        SET BalancePaise -= @AmountPaise, WithdrawnPaise += @AmountPaise, UpdatedAt = GETDATE()
        WHERE PlayerId = @PlayerId;

        DECLARE @NewBalance BIGINT = @Balance - @AmountPaise;
        INSERT INTO Transactions (PlayerId, TxnType, AmountPaise, BalanceAfter, Status, Remarks)
        VALUES (@PlayerId, 'Withdrawal', @AmountPaise, @NewBalance, 'Pending', 'Withdrawal request submitted');

        COMMIT;
        SET @Result = 1; SET @Message = 'Withdrawal request submitted.';
    END TRY
    BEGIN CATCH
        ROLLBACK; SET @Result = -99; SET @Message = ERROR_MESSAGE();
    END CATCH
END
GO

CREATE OR ALTER PROCEDURE USP_GetNotifications
    @PlayerId   UNIQUEIDENTIFIER,
    @PageNo     INT = 1,
    @PageSize   INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@PageNo - 1) * @PageSize;
    SELECT NotifId, Title, Body, NotifType, IsRead, CreatedAt
    FROM Notifications
    WHERE (PlayerId = @PlayerId OR PlayerId IS NULL)
      AND (ISNULL(LTRIM(RTRIM(Title)), '') <> '' OR ISNULL(LTRIM(RTRIM(Body)), '') <> '')
    ORDER BY CreatedAt DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE USP_MarkNotificationRead
    @PlayerId   UNIQUEIDENTIFIER,
    @NotifId    UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE Notifications SET IsRead = 1
    WHERE NotifId = @NotifId AND (PlayerId = @PlayerId OR PlayerId IS NULL);
END
GO

PRINT 'SpeedSaga DB update 002 applied.';
GO
