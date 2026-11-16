-- Campus Library Management System: table definitions for a new empty database.
-- Preserve table-name casing when installing on case-sensitive MySQL servers.
SET NAMES utf8mb4;
SET FOREIGN_KEY_CHECKS=0;

CREATE TABLE `Books` (
  `ItemId` int NOT NULL,
  `Title` varchar(200) NOT NULL,
  `Author` varchar(200) DEFAULT NULL,
  `PublicationYear` int DEFAULT NULL,
  `BookLanguage` varchar(50) NOT NULL,
  `ISBN` varchar(20) NOT NULL,
  PRIMARY KEY (`ItemId`),
  CONSTRAINT `Books_ibfk_1` FOREIGN KEY (`ItemId`) REFERENCES `Inventory` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Faculty` (
  `faculty_id` char(7) NOT NULL,
  `position` varchar(50) DEFAULT NULL,
  `rental_limit` int DEFAULT '10',
  `waitlist_limit` int DEFAULT '10',
  `LibraryUserId` int NOT NULL,
  PRIMARY KEY (`faculty_id`),
  UNIQUE KEY `uq_faculty_library_user` (`LibraryUserId`),
  CONSTRAINT `fk_faculty_library_user` FOREIGN KEY (`LibraryUserId`) REFERENCES `Libraryuser` (`LibraryUserId`) ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Fine` (
  `FineId` int NOT NULL AUTO_INCREMENT,
  `RentalId` int NOT NULL,
  `LibraryUserId` int NOT NULL,
  `FineType` varchar(20) NOT NULL,
  `Amount` decimal(10,2) NOT NULL,
  `DaysLate` int DEFAULT NULL,
  `IsPaid` tinyint(1) NOT NULL DEFAULT '0',
  `DateAssessed` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `AssessedByStaffId` int DEFAULT NULL,
  `Notes` varchar(200) DEFAULT NULL,
  PRIMARY KEY (`FineId`),
  KEY `RentalId` (`RentalId`),
  KEY `LibraryUserId` (`LibraryUserId`),
  CONSTRAINT `Fine_ibfk_1` FOREIGN KEY (`RentalId`) REFERENCES `Rentals` (`RentalId`),
  CONSTRAINT `Fine_ibfk_2` FOREIGN KEY (`LibraryUserId`) REFERENCES `Libraryuser` (`LibraryUserId`),
  CONSTRAINT `chk_fine_amount` CHECK ((`Amount` > 0)),
  CONSTRAINT `chk_fine_type` CHECK ((`FineType` in (_utf8mb4'Overdue',_utf8mb4'Damaged',_utf8mb4'Lost'))),
  CONSTRAINT `Fine_chk_1` CHECK ((`FineType` in (_utf8mb4'Overdue',_utf8mb4'Damaged',_utf8mb4'Lost')))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `FinePolicy` (
  `ItemType` varchar(50) NOT NULL,
  `LoanDays` int NOT NULL,
  `DailyOverdueRate` decimal(10,2) NOT NULL,
  `OverdueCap` decimal(10,2) NOT NULL,
  `DamageFee` decimal(10,2) NOT NULL,
  `ReplacementCost` decimal(10,2) NOT NULL,
  `InspectionEvery` int DEFAULT NULL,
  PRIMARY KEY (`ItemType`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Headphones` (
  `ItemId` int NOT NULL,
  `Brand` varchar(50) NOT NULL,
  `Model` varchar(50) NOT NULL,
  PRIMARY KEY (`ItemId`),
  CONSTRAINT `Headphones_ibfk_1` FOREIGN KEY (`ItemId`) REFERENCES `Inventory` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Hold` (
  `Hold_ID` varchar(6) NOT NULL,
  `LibraryUserId` int NOT NULL,
  `Request_Datetime` datetime NOT NULL,
  `Status` varchar(20) NOT NULL DEFAULT 'Pending',
  `Ready_Date` datetime DEFAULT NULL,
  `Pickup_Deadline` datetime DEFAULT NULL,
  `ItemId` int DEFAULT NULL,
  PRIMARY KEY (`Hold_ID`),
  KEY `LibraryUserId` (`LibraryUserId`),
  KEY `fk_hold_item` (`ItemId`),
  CONSTRAINT `fk_hold_item` FOREIGN KEY (`ItemId`) REFERENCES `Inventory` (`Id`),
  CONSTRAINT `Hold_ibfk_1` FOREIGN KEY (`LibraryUserId`) REFERENCES `Libraryuser` (`LibraryUserId`),
	CONSTRAINT `chk_hold_status` CHECK ((`Status` in (_utf8mb4'Pending',_utf8mb4'Ready',_utf8mb4'Fulfilled',_utf8mb4'Cancelled',_utf8mb4'Expired',_utf8mb4'No Show')))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Notification` (
  `NotificationId` bigint NOT NULL AUTO_INCREMENT,
  `LibraryUserId` int NOT NULL,
  `NotificationType` varchar(50) NOT NULL,
  `RelatedId` varchar(20) NOT NULL,
  `Title` varchar(200) NOT NULL,
  `Message` varchar(500) NOT NULL,
  `CreatedAt` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
	`ReadAt` datetime DEFAULT NULL,
  PRIMARY KEY (`NotificationId`),
  UNIQUE KEY `uq_notification_recipient_event` (`LibraryUserId`,`NotificationType`,`RelatedId`),
  KEY `ix_notification_user_created` (`LibraryUserId`,`CreatedAt`),
  CONSTRAINT `fk_notification_user` FOREIGN KEY (`LibraryUserId`) REFERENCES `Libraryuser` (`LibraryUserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Inventory` (
  `Id` int NOT NULL AUTO_INCREMENT,
  `Type` varchar(50) NOT NULL,
  `Name` varchar(300) NOT NULL,
  `status` varchar(50) NOT NULL,
  `barcode` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL,
  PRIMARY KEY (`Id`),
  UNIQUE KEY `barcode` (`barcode`),
  UNIQUE KEY `uq_inventory_barcode` (`barcode`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Laptops` (
  `ItemId` int NOT NULL,
  `Brand` varchar(50) DEFAULT NULL,
  `Model` varchar(50) DEFAULT NULL,
  `SerialNumber` varchar(50) DEFAULT NULL,
  PRIMARY KEY (`ItemId`),
  CONSTRAINT `Laptops_ibfk_1` FOREIGN KEY (`ItemId`) REFERENCES `Inventory` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Libraryuser` (
  `LibraryUserId` int NOT NULL AUTO_INCREMENT,
  `IsActive` bit(1) NOT NULL,
	`RoomReservationRestricted` bit(1) NOT NULL DEFAULT b'0',
  `UserStatus` varchar(30) DEFAULT NULL,
  `FirstName` varchar(30) NOT NULL,
  `LastName` varchar(30) NOT NULL,
  `MiddleInitial` char(1) DEFAULT NULL,
  `PhoneNumber` varchar(30) DEFAULT NULL,
  `AddressLine1` varchar(150) DEFAULT NULL,
  `AddressLine2` varchar(100) DEFAULT NULL,
  `City` varchar(50) DEFAULT NULL,
  `State` varchar(30) DEFAULT NULL,
  `PostalCode` varchar(20) DEFAULT NULL,
  `FullName` varchar(200) GENERATED ALWAYS AS (concat(`FirstName`,_utf8mb4' ',`LastName`)) VIRTUAL,
  PRIMARY KEY (`LibraryUserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `MaintenanceRequest` (
  `RequestId` int NOT NULL AUTO_INCREMENT,
  `ItemId` int NOT NULL,
  `RentalId` int DEFAULT NULL,
  `RequestType` varchar(30) NOT NULL,
  `Reason` varchar(200) NOT NULL,
  `Status` varchar(20) NOT NULL DEFAULT 'Open',
  `CreatedAt` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `ResolvedAt` datetime DEFAULT NULL,
  `Notes` varchar(200) DEFAULT NULL,
  PRIMARY KEY (`RequestId`),
  KEY `ItemId` (`ItemId`),
  KEY `RentalId` (`RentalId`),
  CONSTRAINT `MaintenanceRequest_ibfk_1` FOREIGN KEY (`ItemId`) REFERENCES `Inventory` (`Id`),
  CONSTRAINT `MaintenanceRequest_ibfk_2` FOREIGN KEY (`RentalId`) REFERENCES `Rentals` (`RentalId`),
  CONSTRAINT `chk_mr_status` CHECK ((`Status` in (_utf8mb4'Open',_utf8mb4'In Progress',_utf8mb4'Resolved',_utf8mb4'Replaced'))),
  CONSTRAINT `chk_mr_type` CHECK ((`RequestType` in (_utf8mb4'Damage Repair',_utf8mb4'Routine Inspection',_utf8mb4'Replacement Review')))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Maps` (
  `ItemId` int NOT NULL,
  `Title` varchar(50) NOT NULL,
  `Region` varchar(200) DEFAULT NULL,
  `Edition` int DEFAULT NULL,
  PRIMARY KEY (`ItemId`),
  CONSTRAINT `Maps_ibfk_1` FOREIGN KEY (`ItemId`) REFERENCES `Inventory` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Media` (
  `ItemId` int NOT NULL,
  `ItemType` varchar(50) NOT NULL,
  `Genre` varchar(50) DEFAULT NULL,
  `Creator` varchar(200) DEFAULT NULL,
  PRIMARY KEY (`ItemId`),
  CONSTRAINT `Media_ibfk_1` FOREIGN KEY (`ItemId`) REFERENCES `Inventory` (`Id`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Payment` (
  `payment_id` char(7) NOT NULL,
  `LibraryUserId` int NOT NULL,
  `amount` decimal(10,2) NOT NULL,
  `payment_date` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `payment_method` varchar(20) NOT NULL,
  `FineId` int DEFAULT NULL,
  PRIMARY KEY (`payment_id`),
  KEY `LibraryUserFK` (`LibraryUserId`),
  KEY `fk_payment_fine` (`FineId`),
  CONSTRAINT `fk_payment_fine` FOREIGN KEY (`FineId`) REFERENCES `Fine` (`FineId`),
  CONSTRAINT `LibraryUserFK` FOREIGN KEY (`LibraryUserId`) REFERENCES `Libraryuser` (`LibraryUserId`),
  CONSTRAINT `check_payment_ammount` CHECK ((`amount` > 0)),
  CONSTRAINT `check_payment_method` CHECK ((`payment_method` in (_utf8mb4'cash',_utf8mb4'card',_utf8mb4'check')))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `ReadingList` (
  `ReadingListId` int NOT NULL AUTO_INCREMENT,
  `LibraryUserId` int NOT NULL,
  `ItemId` int NOT NULL,
  `AddedAt` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (`ReadingListId`),
  UNIQUE KEY `uq_readinglist` (`LibraryUserId`,`ItemId`),
  KEY `fk_readinglist_item` (`ItemId`),
  CONSTRAINT `fk_readinglist_item` FOREIGN KEY (`ItemId`) REFERENCES `Inventory` (`Id`),
  CONSTRAINT `fk_readinglist_user` FOREIGN KEY (`LibraryUserId`) REFERENCES `Libraryuser` (`LibraryUserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Rentals` (
  `RentalId` int NOT NULL AUTO_INCREMENT,
  `ItemId` int NOT NULL,
  `StartDate` datetime NOT NULL,
  `EndDate` datetime NOT NULL,
  `ReturnedDate` datetime DEFAULT NULL,
  `IsReturned` bit(1) NOT NULL,
  `LibraryUserId` int NOT NULL,
  `ReturnCondition` varchar(20) DEFAULT NULL,
  `DroppedOffAt` datetime DEFAULT NULL,
  PRIMARY KEY (`RentalId`),
  KEY `LibraryUserId` (`LibraryUserId`),
  KEY `fk_ItemId` (`ItemId`),
  CONSTRAINT `fk_ItemId` FOREIGN KEY (`ItemId`) REFERENCES `Inventory` (`Id`),
  CONSTRAINT `Rentals_ibfk_1` FOREIGN KEY (`LibraryUserId`) REFERENCES `Libraryuser` (`LibraryUserId`),
  CONSTRAINT `chk_return_condition` CHECK (((`ReturnCondition` is null) or (`ReturnCondition` in (_utf8mb4'Good',_utf8mb4'Damaged',_utf8mb4'Lost'))))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Room` (
  `room_id` char(5) NOT NULL,
  `capacity` int NOT NULL,
  `status` varchar(10) DEFAULT NULL,
  `active` bit(1) DEFAULT NULL,
  PRIMARY KEY (`room_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Room_reservation` (
  `reservation_id` char(7) NOT NULL,
  `room_id` char(5) NOT NULL,
  `start_time` datetime NOT NULL,
  `end_time` datetime NOT NULL,
  `num_of_people` int NOT NULL,
  `LibraryUserId` int NOT NULL,
  `purpose` varchar(200) NOT NULL DEFAULT '',
  `status` varchar(20) NOT NULL DEFAULT 'Confirmed',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `canceled_at` datetime DEFAULT NULL,
  PRIMARY KEY (`reservation_id`),
  KEY `room_id` (`room_id`),
  KEY `ix_room_reservation_user` (`LibraryUserId`),
  KEY `ix_room_reservation_room_time` (`room_id`,`start_time`,`end_time`),
  CONSTRAINT `fk_room_reservation_user` FOREIGN KEY (`LibraryUserId`) REFERENCES `Libraryuser` (`LibraryUserId`),
  CONSTRAINT `Room_reservation_ibfk_1` FOREIGN KEY (`room_id`) REFERENCES `Room` (`room_id`),
  CONSTRAINT `check_min_people` CHECK ((`num_of_people` >= 1)),
	CONSTRAINT `check_reservation_status` CHECK ((`status` in (_utf8mb4'Confirmed',_utf8mb4'Checked In',_utf8mb4'Cancelled',_utf8mb4'Completed',_utf8mb4'No Show')))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Room_setup_task` (
  `task_id` int NOT NULL AUTO_INCREMENT,
  `reservation_id` char(7) NOT NULL,
  `room_id` char(5) NOT NULL,
  `task_description` varchar(255) NOT NULL,
  `status` varchar(20) NOT NULL DEFAULT 'Open',
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `completed_at` datetime DEFAULT NULL,
  PRIMARY KEY (`task_id`),
  KEY `reservation_id` (`reservation_id`),
  KEY `room_id` (`room_id`),
  CONSTRAINT `Room_setup_task_ibfk_1` FOREIGN KEY (`reservation_id`) REFERENCES `Room_reservation` (`reservation_id`),
  CONSTRAINT `Room_setup_task_ibfk_2` FOREIGN KEY (`room_id`) REFERENCES `Room` (`room_id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Staff` (
  `Staff_Id` int NOT NULL AUTO_INCREMENT,
  `StartDate` date NOT NULL,
  `IsActive` tinyint(1) NOT NULL DEFAULT '1',
  `LibraryUserId` int NOT NULL,
  `JobTitle` varchar(30) NOT NULL,
  `RentalLimit` int NOT NULL DEFAULT '20',
  `WaitlistLimit` int NOT NULL DEFAULT '20',
  CONSTRAINT `chk_staff_rental_limit` CHECK (`RentalLimit` >= 0),
  CONSTRAINT `chk_staff_waitlist_limit` CHECK (`WaitlistLimit` >= 0),
  PRIMARY KEY (`Staff_Id`),
  UNIQUE KEY `uq_staff_library_user` (`LibraryUserId`),
  CONSTRAINT `fk_staff_library_user` FOREIGN KEY (`LibraryUserId`) REFERENCES `Libraryuser` (`LibraryUserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `Student` (
  `Student_ID` varchar(7) NOT NULL,
  `Registration_Date` datetime NOT NULL,
  `AcademicLevel` varchar(20) DEFAULT NULL,
  `DateOfBirth` date DEFAULT NULL,
  `EnrollStatus` varchar(50) DEFAULT NULL,
  `RentalLimit` int DEFAULT NULL,
  `WaitlistLimit` int DEFAULT NULL,
  `LibraryUserId` int NOT NULL,
  PRIMARY KEY (`Student_ID`),
  UNIQUE KEY `uq_student_library_user` (`LibraryUserId`),
  CONSTRAINT `fk_student_library_user` FOREIGN KEY (`LibraryUserId`) REFERENCES `Libraryuser` (`LibraryUserId`) ON DELETE RESTRICT ON UPDATE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE `UserAccount` (
  `AccountId` int NOT NULL AUTO_INCREMENT,
  `Email` varchar(255) NOT NULL,
  `NormalizedEmail` varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL,
  `PasswordHash` varchar(512) NOT NULL,
  `Role` varchar(20) NOT NULL DEFAULT 'Student',
  `IsEnabled` tinyint(1) NOT NULL DEFAULT '1',
  `LibraryUserId` int NOT NULL,
  `FailedLoginAttempts` int NOT NULL DEFAULT '0',
  `LockoutUntilUtc` datetime(6) DEFAULT NULL,
  `SessionVersion` int NOT NULL DEFAULT '1',
  PRIMARY KEY (`AccountId`),
  UNIQUE KEY `uq_account_email` (`NormalizedEmail`),
  UNIQUE KEY `uq_account_borrower` (`LibraryUserId`),
  CONSTRAINT `fk_account_borrower` FOREIGN KEY (`LibraryUserId`) REFERENCES `Libraryuser` (`LibraryUserId`),
  CONSTRAINT `chk_account_role` CHECK ((`Role` in (_utf8mb4'Student',_utf8mb4'Faculty',_utf8mb4'Staff',_utf8mb4'Admin')))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

SET FOREIGN_KEY_CHECKS=1;
-- Install triggers.sql after importing any initial data.
