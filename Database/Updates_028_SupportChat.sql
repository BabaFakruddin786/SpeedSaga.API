IF OBJECT_ID('SupportMessages', 'U') IS NULL
BEGIN
    CREATE TABLE SupportMessages (
        MessageId   UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
        TicketId    UNIQUEIDENTIFIER NOT NULL,
        SenderType  NVARCHAR(10)     NOT NULL,
        Body        NVARCHAR(2000)   NOT NULL,
        CreatedAt   DATETIME         NOT NULL DEFAULT GETDATE()
    );
END
GO

IF OBJECT_ID('SupportTickets', 'U') IS NULL
BEGIN
    CREATE TABLE SupportTickets (
        TicketId    UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID() PRIMARY KEY,
        PlayerId    UNIQUEIDENTIFIER NOT NULL REFERENCES Players(PlayerId),
        Status      NVARCHAR(20)     NOT NULL DEFAULT 'AwaitingAgent',
        Subject     NVARCHAR(200)    NULL,
        CreatedAt   DATETIME         NOT NULL DEFAULT GETDATE(),
        UpdatedAt   DATETIME         NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_SupportTickets_PlayerStatus ON SupportTickets(PlayerId, Status);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_SupportMessages_Ticket' AND object_id = OBJECT_ID('SupportMessages'))
    CREATE INDEX IX_SupportMessages_Ticket ON SupportMessages(TicketId, CreatedAt);
GO

CREATE OR ALTER PROCEDURE USP_GetOpenSupportTicket
    @PlayerId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT TOP 1 TicketId, Status, Subject, CreatedAt, UpdatedAt
    FROM SupportTickets
    WHERE PlayerId = @PlayerId AND Status <> 'Closed'
    ORDER BY UpdatedAt DESC;
END
GO

CREATE OR ALTER PROCEDURE USP_GetSupportMessages
    @TicketId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SELECT MessageId, SenderType, Body, CreatedAt
    FROM SupportMessages
    WHERE TicketId = @TicketId
    ORDER BY CreatedAt ASC;
END
GO

CREATE OR ALTER PROCEDURE USP_CreateSupportTicket
    @PlayerId    UNIQUEIDENTIFIER,
    @Subject     NVARCHAR(200),
    @InitialBody NVARCHAR(2000),
    @TicketId    UNIQUEIDENTIFIER OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET @TicketId = NEWID();
    INSERT INTO SupportTickets (TicketId, PlayerId, Status, Subject)
    VALUES (@TicketId, @PlayerId, 'AwaitingAgent', @Subject);
    INSERT INTO SupportMessages (TicketId, SenderType, Body)
    VALUES (@TicketId, 'Player', @InitialBody);
END
GO

CREATE OR ALTER PROCEDURE USP_AddSupportMessage
    @TicketId   UNIQUEIDENTIFIER,
    @SenderType NVARCHAR(10),
    @Body       NVARCHAR(2000)
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO SupportMessages (TicketId, SenderType, Body)
    VALUES (@TicketId, @SenderType, @Body);
    UPDATE SupportTickets SET
        UpdatedAt = GETDATE(),
        Status = CASE
            WHEN @SenderType = 'Player' THEN 'AwaitingAgent'
            WHEN @SenderType = 'Agent' THEN 'AwaitingPlayer'
            ELSE Status END
    WHERE TicketId = @TicketId;
END
GO

CREATE OR ALTER PROCEDURE USP_ListSupportTickets
    @Status NVARCHAR(20) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SELECT T.TicketId, T.PlayerId, P.Username, P.ContactPhone, P.ContactEmail,
           T.Status, T.Subject, T.CreatedAt, T.UpdatedAt,
           (SELECT TOP 1 Body FROM SupportMessages M WHERE M.TicketId = T.TicketId ORDER BY CreatedAt DESC) AS LastMessage
    FROM SupportTickets T
    INNER JOIN Players P ON P.PlayerId = T.PlayerId
    WHERE (@Status IS NULL OR T.Status = @Status)
      AND T.Status <> 'Closed'
    ORDER BY T.UpdatedAt DESC;
END
GO

CREATE OR ALTER PROCEDURE USP_CloseSupportTicket
    @TicketId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE SupportTickets SET Status = 'Closed', UpdatedAt = GETDATE()
    WHERE TicketId = @TicketId;
END
GO
