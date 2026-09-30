CREATE OR ALTER TRIGGER TR_Payments_DenyUpdateDelete
ON dbo.Payments
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51000, 'dbo.Payments is append-only. Record a reversal instead.', 1;
END;
