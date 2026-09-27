CREATE TRIGGER TR_PaymentAllocations_DenyUpdateDelete
ON dbo.PaymentAllocations
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    RAISERROR('Update and Delete operations are not allowed on this table.', 16, 1);
    ROLLBACK TRANSACTION;
END;
