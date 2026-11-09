-- Run once on databases that do not yet have the employee-to-borrower link.
ALTER TABLE Staff
    ADD COLUMN LibraryUserId INT NULL,
    ADD CONSTRAINT uq_staff_library_user UNIQUE (LibraryUserId),
    ADD CONSTRAINT fk_staff_library_user FOREIGN KEY (LibraryUserId)
        REFERENCES Libraryuser (LibraryUserId);
