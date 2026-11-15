-- Apply once to the MySQL database before deploying the cover-image feature.
ALTER TABLE `Books`
    ADD COLUMN `CoverImage` MEDIUMBLOB NULL,
    ADD COLUMN `CoverImageContentType` VARCHAR(30) NULL;
