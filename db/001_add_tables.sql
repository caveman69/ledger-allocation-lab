CREATE TABLE dbo.Districts (
    Id      INT           NOT NULL PRIMARY KEY,
    Name    NVARCHAR(100) NOT NULL
);

CREATE TABLE dbo.Parcels (
    Id              INT          NOT NULL PRIMARY KEY,
    ParcelNumber    VARCHAR(20)  NOT NULL UNIQUE
);

CREATE TABLE dbo.ParcelDistrictRates (
    Id           BIGINT IDENTITY PRIMARY KEY,
    ParcelId     INT            NOT NULL REFERENCES dbo.Parcels(Id),
    DistrictId   INT            NOT NULL REFERENCES dbo.Districts(Id),
    TaxYear      SMALLINT       NOT NULL,
    Rate         DECIMAL(9, 6)  NOT NULL CHECK (Rate >= 0),
    CONSTRAINT UQ_ParcelId_DistrictId_TaxYear UNIQUE (ParcelId, DistrictId, TaxYear)
);

CREATE TABLE dbo.Payments (
    Id               BIGINT IDENTITY PRIMARY KEY,
    IdempotencyKey   UNIQUEIDENTIFIER NOT NULL,
    ParcelId         INT          NOT NULL REFERENCES dbo.Parcels(Id),
    TaxYear          SMALLINT     NOT NULL,
    AmountCents      BIGINT       NOT NULL,          -- negative for a reversal
    ReversesPaymentId BIGINT      NULL REFERENCES dbo.Payments(Id),
    ReceivedOnUtc      DATETIME2(3) NOT NULL,
    BusinessDate     DATE         NOT NULL,
    CONSTRAINT UQ_Payment_IdempotencyKey UNIQUE (IdempotencyKey)
);

CREATE TABLE dbo.PaymentAllocations (
    Id           BIGINT NOT NULL IDENTITY PRIMARY KEY,
    PaymentId    BIGINT NOT NULL REFERENCES dbo.Payments(Id),
    DistrictId   INT    NOT NULL REFERENCES dbo.Districts(Id),
    AmountCents  BIGINT NOT NULL,
    CONSTRAINT UQ_PaymentId_DistrictId UNIQUE (PaymentId, DistrictId)
);
