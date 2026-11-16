-- Apply to an existing database after deploying the no-show and room check-in changes.
ALTER TABLE `Libraryuser`
	ADD COLUMN IF NOT EXISTS `RoomReservationRestricted` bit(1) NOT NULL DEFAULT b'0';

ALTER TABLE `Hold`
	DROP CHECK `chk_hold_status`,
	ADD CONSTRAINT `chk_hold_status`
		CHECK (`Status` IN ('Pending', 'Ready', 'Fulfilled', 'Cancelled', 'Expired', 'No Show'));

ALTER TABLE `Room_reservation`
	DROP CHECK `check_reservation_status`,
	ADD CONSTRAINT `check_reservation_status`
		CHECK (`status` IN ('Confirmed', 'Checked In', 'Cancelled', 'Completed', 'No Show'));

CREATE TABLE IF NOT EXISTS `Notification` (
	`NotificationId` bigint NOT NULL AUTO_INCREMENT,
	`LibraryUserId` int NOT NULL,
	`NotificationType` varchar(50) NOT NULL,
	`RelatedId` varchar(20) NOT NULL,
	`Title` varchar(200) NOT NULL,
	`Message` varchar(500) NOT NULL,
	`CreatedAt` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
	`ReadAt` datetime DEFAULT NULL,
	PRIMARY KEY (`NotificationId`),
	UNIQUE KEY `uq_notification_recipient_event` (`LibraryUserId`, `NotificationType`, `RelatedId`),
	KEY `ix_notification_user_created` (`LibraryUserId`, `CreatedAt`),
	CONSTRAINT `fk_notification_user` FOREIGN KEY (`LibraryUserId`) REFERENCES `Libraryuser` (`LibraryUserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
