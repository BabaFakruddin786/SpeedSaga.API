USE SpeedSagaDB;
GO
SET QUOTED_IDENTIFIER ON;
GO

-- Outgoing SMS / email audit log (every OTP and transactional message)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'OutgoingMessages')
BEGIN
    CREATE TABLE OutgoingMessages (
        MessageId       UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
        PlayerId        UNIQUEIDENTIFIER NULL,
        Channel         NVARCHAR(10)     NOT NULL,  -- SMS, Email
        Purpose         NVARCHAR(40)     NOT NULL,  -- KycAadhaar, PasswordReset, ...
        Destination     NVARCHAR(150)    NOT NULL,  -- phone or email (delivery)
        DestinationMask NVARCHAR(150)    NOT NULL,  -- masked for client display
        BodyPreview     NVARCHAR(500)    NOT NULL,
        Provider        NVARCHAR(30)     NOT NULL DEFAULT 'Dev',
        ProviderRefId   NVARCHAR(120)    NULL,
        Status          NVARCHAR(20)     NOT NULL DEFAULT 'Queued', -- Queued, Sending, Sent, Delivered, Failed
        StatusDetail    NVARCHAR(500)    NULL,
        OtpSessionId    UNIQUEIDENTIFIER NULL,
        CreatedAt       DATETIME         NOT NULL DEFAULT GETDATE(),
        SentAt          DATETIME         NULL,
        DeliveredAt     DATETIME         NULL
    );
    CREATE INDEX IX_OutMsg_Player ON OutgoingMessages(PlayerId, CreatedAt DESC);
    CREATE INDEX IX_OutMsg_Status ON OutgoingMessages(Status, CreatedAt DESC);
    CREATE INDEX IX_OutMsg_Session ON OutgoingMessages(OtpSessionId);
END
GO

-- Persistent OTP sessions (multi-instance safe, survives restarts)
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'OtpSessions')
BEGIN
    CREATE TABLE OtpSessions (
        SessionId       UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
        PlayerId        UNIQUEIDENTIFIER NULL,
        Purpose         NVARCHAR(40)     NOT NULL,
        Channel         NVARCHAR(10)     NOT NULL,
        Destination     NVARCHAR(150)    NOT NULL,
        OtpHash         NVARCHAR(128)    NOT NULL,
        OtpSalt         NVARCHAR(64)     NOT NULL,
        ContextJson     NVARCHAR(1000)   NULL,
        ExpiresAt       DATETIME         NOT NULL,
        AttemptCount    INT              NOT NULL DEFAULT 0,
        MaxAttempts     INT              NOT NULL DEFAULT 5,
        IsVerified      BIT              NOT NULL DEFAULT 0,
        IsRevoked       BIT              NOT NULL DEFAULT 0,
        MessageId       UNIQUEIDENTIFIER NULL,
        CreatedAt       DATETIME         NOT NULL DEFAULT GETDATE(),
        LastAttemptAt   DATETIME         NULL,
        VerifiedAt      DATETIME         NULL
    );
    CREATE INDEX IX_Otp_Player_Purpose ON OtpSessions(PlayerId, Purpose, CreatedAt DESC);
    CREATE INDEX IX_Otp_Dest_Purpose ON OtpSessions(Destination, Purpose, CreatedAt DESC);
    CREATE INDEX IX_Otp_Expires ON OtpSessions(ExpiresAt) WHERE IsVerified = 0 AND IsRevoked = 0;
END
GO

CREATE OR ALTER PROCEDURE USP_CheckOtpRateLimit
    @PlayerId           UNIQUEIDENTIFIER = NULL,
    @Destination        NVARCHAR(150),
    @Purpose            NVARCHAR(40),
    @WindowMinutes      INT = 15,
    @MaxSends           INT = 3,
    @CooldownSeconds    INT = 60,
    @Allowed            BIT OUTPUT,
    @RetryAfterSeconds  INT OUTPUT,
    @Message            NVARCHAR(200) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Allowed = 1;
    SET @RetryAfterSeconds = 0;
    SET @Message = 'OK';

    DECLARE @Since DATETIME = DATEADD(MINUTE, -@WindowMinutes, GETDATE());
    DECLARE @SendCount INT;
    DECLARE @LastSent DATETIME;

    SELECT @SendCount = COUNT(*), @LastSent = MAX(CreatedAt)
    FROM OtpSessions
    WHERE Purpose = @Purpose
      AND Destination = @Destination
      AND CreatedAt >= @Since
      AND (@PlayerId IS NULL OR PlayerId = @PlayerId);

    IF @SendCount >= @MaxSends
    BEGIN
        SET @Allowed = 0;
        SET @Message = 'Too many OTP requests. Please try again later.';
        SET @RetryAfterSeconds = DATEDIFF(SECOND, GETDATE(), DATEADD(MINUTE, @WindowMinutes, @LastSent));
        IF @RetryAfterSeconds < 0 SET @RetryAfterSeconds = 60;
        RETURN;
    END

    IF @LastSent IS NOT NULL AND DATEDIFF(SECOND, @LastSent, GETDATE()) < @CooldownSeconds
    BEGIN
        SET @Allowed = 0;
        SET @RetryAfterSeconds = @CooldownSeconds - DATEDIFF(SECOND, @LastSent, GETDATE());
        SET @Message = 'Please wait before requesting another OTP.';
    END
END
GO

CREATE OR ALTER PROCEDURE USP_RevokeOpenOtpSessions
    @PlayerId       UNIQUEIDENTIFIER = NULL,
    @Destination    NVARCHAR(150),
    @Purpose        NVARCHAR(40)
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE OtpSessions
    SET IsRevoked = 1
    WHERE Purpose = @Purpose
      AND Destination = @Destination
      AND IsVerified = 0
      AND IsRevoked = 0
      AND ExpiresAt > GETDATE()
      AND (@PlayerId IS NULL OR PlayerId = @PlayerId);
END
GO

CREATE OR ALTER PROCEDURE USP_CreateOtpSession
    @SessionId      UNIQUEIDENTIFIER OUTPUT,
    @PlayerId       UNIQUEIDENTIFIER = NULL,
    @Purpose        NVARCHAR(40),
    @Channel        NVARCHAR(10),
    @Destination    NVARCHAR(150),
    @OtpHash        NVARCHAR(128),
    @OtpSalt        NVARCHAR(64),
    @ContextJson    NVARCHAR(1000) = NULL,
    @ExpiresAt      DATETIME,
    @MaxAttempts    INT = 5,
    @MessageId      UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @SessionId = NEWID();

    EXEC USP_RevokeOpenOtpSessions @PlayerId, @Destination, @Purpose;

    INSERT INTO OtpSessions (
        SessionId, PlayerId, Purpose, Channel, Destination,
        OtpHash, OtpSalt, ContextJson, ExpiresAt, MaxAttempts, MessageId)
    VALUES (
        @SessionId, @PlayerId, @Purpose, @Channel, @Destination,
        @OtpHash, @OtpSalt, @ContextJson, @ExpiresAt, @MaxAttempts, @MessageId);
END
GO

CREATE OR ALTER PROCEDURE USP_InsertOutgoingMessage
    @MessageId          UNIQUEIDENTIFIER OUTPUT,
    @PlayerId           UNIQUEIDENTIFIER = NULL,
    @Channel            NVARCHAR(10),
    @Purpose            NVARCHAR(40),
    @Destination        NVARCHAR(150),
    @DestinationMask    NVARCHAR(150),
    @BodyPreview        NVARCHAR(500),
    @Provider           NVARCHAR(30),
    @OtpSessionId       UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET @MessageId = NEWID();
    INSERT INTO OutgoingMessages (
        MessageId, PlayerId, Channel, Purpose, Destination, DestinationMask,
        BodyPreview, Provider, Status, OtpSessionId)
    VALUES (
        @MessageId, @PlayerId, @Channel, @Purpose, @Destination, @DestinationMask,
        @BodyPreview, @Provider, 'Queued', @OtpSessionId);
END
GO

CREATE OR ALTER PROCEDURE USP_UpdateOutgoingMessageStatus
    @MessageId      UNIQUEIDENTIFIER,
    @Status         NVARCHAR(20),
    @StatusDetail   NVARCHAR(500) = NULL,
    @ProviderRefId  NVARCHAR(120) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE OutgoingMessages
    SET Status = @Status,
        StatusDetail = @StatusDetail,
        ProviderRefId = COALESCE(@ProviderRefId, ProviderRefId),
        SentAt = CASE WHEN @Status IN ('Sent', 'Delivered') AND SentAt IS NULL THEN GETDATE() ELSE SentAt END,
        DeliveredAt = CASE WHEN @Status = 'Delivered' THEN GETDATE() ELSE DeliveredAt END
    WHERE MessageId = @MessageId;
END
GO

CREATE OR ALTER PROCEDURE USP_VerifyOtpSession
    @SessionId      UNIQUEIDENTIFIER = NULL,
    @PlayerId       UNIQUEIDENTIFIER = NULL,
    @Destination    NVARCHAR(150) = NULL,
    @Purpose        NVARCHAR(40),
    @OtpHash        NVARCHAR(128),
    @Result         INT OUTPUT,
    @Message        NVARCHAR(200) OUTPUT,
    @ContextJson    NVARCHAR(1000) OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @Result = 0;
    SET @Message = 'Invalid or expired OTP.';
    SET @ContextJson = NULL;

    DECLARE @Sid UNIQUEIDENTIFIER, @StoredHash NVARCHAR(128), @Attempts INT, @MaxAttempts INT,
            @ExpiresAt DATETIME, @IsVerified BIT, @IsRevoked BIT, @Ctx NVARCHAR(1000), @SessPlayer UNIQUEIDENTIFIER;

    IF @SessionId IS NOT NULL
        SELECT TOP 1 @Sid = SessionId, @StoredHash = OtpHash, @Attempts = AttemptCount, @MaxAttempts = MaxAttempts,
               @ExpiresAt = ExpiresAt, @IsVerified = IsVerified, @IsRevoked = IsRevoked,
               @ContextJson = ContextJson, @SessPlayer = PlayerId
        FROM OtpSessions WHERE SessionId = @SessionId;
    ELSE
        SELECT TOP 1 @Sid = SessionId, @StoredHash = OtpHash, @Attempts = AttemptCount, @MaxAttempts = MaxAttempts,
               @ExpiresAt = ExpiresAt, @IsVerified = IsVerified, @IsRevoked = IsRevoked,
               @ContextJson = ContextJson, @SessPlayer = PlayerId
        FROM OtpSessions
        WHERE Purpose = @Purpose AND Destination = @Destination
          AND IsVerified = 0 AND IsRevoked = 0
        ORDER BY CreatedAt DESC;

    IF @Sid IS NULL BEGIN SET @Result = -1; SET @Message = 'OTP expired or invalid. Request a new OTP.'; RETURN; END
    IF @IsVerified = 1 BEGIN SET @Result = -2; SET @Message = 'OTP already used.'; RETURN; END
    IF @IsRevoked = 1 BEGIN SET @Result = -3; SET @Message = 'OTP expired or invalid. Request a new OTP.'; RETURN; END
    IF @ExpiresAt <= GETDATE() BEGIN SET @Result = -4; SET @Message = 'OTP expired. Request a new OTP.'; RETURN; END
    IF @PlayerId IS NOT NULL AND @SessPlayer IS NOT NULL AND @SessPlayer <> @PlayerId
    BEGIN SET @Result = -5; SET @Message = 'Invalid verification session.'; RETURN; END
    IF @Attempts >= @MaxAttempts BEGIN SET @Result = -6; SET @Message = 'Too many failed attempts. Request a new OTP.'; RETURN; END

    UPDATE OtpSessions SET AttemptCount = AttemptCount + 1, LastAttemptAt = GETDATE() WHERE SessionId = @Sid;

    IF @StoredHash <> @OtpHash
    BEGIN
        SET @Result = -7;
        SET @Message = 'Incorrect OTP.';
        SET @ContextJson = NULL;
        RETURN;
    END

    UPDATE OtpSessions SET IsVerified = 1, VerifiedAt = GETDATE() WHERE SessionId = @Sid;
    SET @Result = 1;
    SET @Message = 'OTP verified.';
END
GO

CREATE OR ALTER PROCEDURE USP_GetPlayerOutgoingMessages
    @PlayerId   UNIQUEIDENTIFIER,
    @PageNo     INT = 1,
    @PageSize   INT = 20
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Offset INT = (@PageNo - 1) * @PageSize;

    SELECT MessageId, Channel, Purpose, DestinationMask, BodyPreview,
           Provider, Status, StatusDetail, CreatedAt, SentAt, DeliveredAt
    FROM OutgoingMessages
    WHERE PlayerId = @PlayerId
    ORDER BY CreatedAt DESC
    OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
END
GO

CREATE OR ALTER PROCEDURE USP_GetOutgoingMessageById
    @MessageId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT MessageId, PlayerId, Channel, Purpose, Destination, DestinationMask,
           BodyPreview, Provider, ProviderRefId, Status, StatusDetail,
           OtpSessionId, CreatedAt, SentAt, DeliveredAt
    FROM OutgoingMessages WHERE MessageId = @MessageId;
END
GO

CREATE OR ALTER PROCEDURE USP_CleanupExpiredOtpSessions
AS
BEGIN
    SET NOCOUNT ON;
    DELETE FROM OtpSessions
    WHERE (ExpiresAt < DATEADD(DAY, -1, GETDATE()))
       OR (IsVerified = 1 AND VerifiedAt < DATEADD(DAY, -7, GETDATE()))
       OR (IsRevoked = 1 AND CreatedAt < DATEADD(DAY, -1, GETDATE()));
END
GO

PRINT 'Updates_013_OtpMessaging applied.';
GO
