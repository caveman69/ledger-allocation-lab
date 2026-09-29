CREATE OR ALTER TRIGGER TR_Payments_DenyUpdateDelete
ON dbo.Payments
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    RAISERROR('Update and Delete operations are not allowed on this table.', 16, 1);
    ROLLBACK TRANSACTION;
END;
