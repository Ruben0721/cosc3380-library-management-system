-- Apply once to the existing library database before deploying this version.
ALTER TABLE Inventory ADD COLUMN RetiredAt DATETIME NULL DEFAULT NULL;
