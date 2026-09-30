CREATE OR ALTER TRIGGER TR_PaymentAllocations_DenyUpdateDelete
ON dbo.PaymentAllocations
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51001, 'dbo.PaymentAllocations is append-only. Record a reversal instead.', 1;
END;
