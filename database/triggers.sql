-- Campus Library Management System: trigger definitions without a fixed DEFINER.
-- Install after importing the schema and initial data into a new database.
DELIMITER $$

CREATE TRIGGER `ValidateHoldLimit` BEFORE INSERT ON `Hold` FOR EACH ROW BEGIN
    DECLARE v_limit INT DEFAULT NULL;
    DECLARE v_count INT DEFAULT 0;
    SELECT CASE a.Role
        WHEN 'Student' THEN s.WaitlistLimit
        WHEN 'Faculty' THEN f.waitlist_limit
        WHEN 'Staff' THEN st.WaitlistLimit
        WHEN 'Admin' THEN st.WaitlistLimit
    END INTO v_limit
    FROM UserAccount a
    LEFT JOIN Student s ON s.LibraryUserId=a.LibraryUserId
    LEFT JOIN Faculty f ON f.LibraryUserId=a.LibraryUserId
    LEFT JOIN Staff st ON st.LibraryUserId=a.LibraryUserId
    WHERE a.LibraryUserId=NEW.LibraryUserId;
    IF v_limit IS NULL OR v_limit < 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Hold rejected: borrower hold limit is not configured';
    END IF;
    SELECT COUNT(*) INTO v_count FROM `Hold`
    WHERE LibraryUserId=NEW.LibraryUserId AND Status IN ('Pending','Ready');
    IF v_count >= v_limit THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Hold rejected: borrower has reached the active hold limit';
    END IF;
END$$

CREATE TRIGGER `trg_maintenance_resolved` BEFORE UPDATE ON `MaintenanceRequest` FOR EACH ROW BEGIN
    DECLARE v_other_open   INT;
    DECLARE v_active_loans INT;

    IF NEW.Status IN ('Resolved', 'Replaced') AND OLD.Status NOT IN ('Resolved', 'Replaced') THEN
        SET NEW.ResolvedAt = COALESCE(NEW.ResolvedAt, NOW());

        IF NEW.Status = 'Resolved' THEN
            SELECT COUNT(*) INTO v_other_open
              FROM MaintenanceRequest
             WHERE ItemId = NEW.ItemId
               AND RequestId <> NEW.RequestId
               AND Status IN ('Open', 'In Progress');

            SELECT COUNT(*) INTO v_active_loans
              FROM Rentals
             WHERE ItemId = NEW.ItemId AND IsReturned = b'0';

            IF v_other_open = 0 AND v_active_loans = 0 THEN
                UPDATE Inventory SET Status = 'Available' WHERE Id = NEW.ItemId;
            END IF;
        END IF;
    END IF;
END$$

CREATE TRIGGER `ValidateRentalLimit` BEFORE INSERT ON `Rentals` FOR EACH ROW BEGIN
    DECLARE v_limit INT DEFAULT NULL;
    DECLARE v_count INT DEFAULT 0;
    SELECT CASE a.Role
        WHEN 'Student' THEN s.RentalLimit
        WHEN 'Faculty' THEN f.rental_limit
        WHEN 'Staff' THEN st.RentalLimit
        WHEN 'Admin' THEN st.RentalLimit
    END INTO v_limit
    FROM UserAccount a
    LEFT JOIN Student s ON s.LibraryUserId=a.LibraryUserId
    LEFT JOIN Faculty f ON f.LibraryUserId=a.LibraryUserId
    LEFT JOIN Staff st ON st.LibraryUserId=a.LibraryUserId
    WHERE a.LibraryUserId=NEW.LibraryUserId;
    IF v_limit IS NULL OR v_limit < 0 THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Rental rejected: borrower rental limit is not configured';
    END IF;
    SELECT COUNT(*) INTO v_count FROM `Rentals`
    WHERE LibraryUserId=NEW.LibraryUserId AND IsReturned = b'0';
    IF v_count >= v_limit THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Rental rejected: borrower has reached the rental limit';
    END IF;
END$$

CREATE TRIGGER `ValidateRentalBeforeInsert` BEFORE INSERT ON `Rentals` FOR EACH ROW BEGIN
    DECLARE user_active BIT(1);
    DECLARE item_status VARCHAR(50);
    DECLARE active_rentals INT DEFAULT 0;

    SET user_active = (
        SELECT IsActive
        FROM Libraryuser
        WHERE LibraryUserId = NEW.LibraryUserId
    );

    IF user_active IS NULL OR user_active <> b'1' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Rental rejected: borrower is inactive or does not exist';
    END IF;

    SET item_status = (
        SELECT status
        FROM Inventory
        WHERE Id = NEW.ItemId
    );

    IF item_status IS NULL OR item_status <> 'Available' THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Rental rejected: item is unavailable or under repair';
    END IF;

    SET active_rentals = (
        SELECT COUNT(*)
        FROM `Rentals`
        WHERE ItemId = NEW.ItemId
          AND IsReturned = b'0'
    );

    IF active_rentals > 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Rental rejected: item is already checked out';
    END IF;

    IF NEW.StartDate IS NULL
       OR NEW.EndDate IS NULL
       OR NEW.EndDate <= NEW.StartDate THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Rental rejected: invalid checkout or due date';
    END IF;
END$$

CREATE TRIGGER `trg_block_rental_unpaid_fines` BEFORE INSERT ON `Rentals` FOR EACH ROW BEGIN
    DECLARE v_charged  DECIMAL(10,2);
    DECLARE v_accruing DECIMAL(10,2);


    SELECT COALESCE(SUM(Amount), 0) INTO v_charged
      FROM Fine
     WHERE LibraryUserId = NEW.LibraryUserId
       AND IsPaid = 0;


    SELECT COALESCE(SUM(LEAST(DATEDIFF(COALESCE(r.DroppedOffAt, NOW()), r.EndDate) * p.DailyOverdueRate,
                              p.OverdueCap)), 0)
      INTO v_accruing
      FROM Rentals r
      JOIN Inventory i  ON i.Id = r.ItemId
      JOIN FinePolicy p ON p.ItemType = i.Type
     WHERE r.LibraryUserId = NEW.LibraryUserId
       AND r.IsReturned = b'0'
       AND DATEDIFF(COALESCE(r.DroppedOffAt, NOW()), r.EndDate) > 0;

    IF v_charged + v_accruing > 10.00 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Checkout rejected: borrower owes more than $10.00 in fines and late fees';
    END IF;
END$$

CREATE TRIGGER `trg_rental_return_fines` AFTER UPDATE ON `Rentals` FOR EACH ROW BEGIN
    DECLARE v_type      VARCHAR(50);
    DECLARE v_rate      DECIMAL(10,2);
    DECLARE v_cap       DECIMAL(10,2);
    DECLARE v_damage    DECIMAL(10,2);
    DECLARE v_replace   DECIMAL(10,2);
    DECLARE v_days_late INT;

    IF OLD.IsReturned = b'0' AND NEW.IsReturned = b'1' AND NEW.ReturnedDate IS NOT NULL THEN

        SELECT Type INTO v_type FROM Inventory WHERE Id = NEW.ItemId;

        SELECT DailyOverdueRate, OverdueCap, DamageFee, ReplacementCost
          INTO v_rate, v_cap, v_damage, v_replace
          FROM FinePolicy WHERE ItemType = v_type;

        SET v_rate    = COALESCE(v_rate, 0.25);
        SET v_cap     = COALESCE(v_cap, 15.00);
        SET v_damage  = COALESCE(v_damage, 20.00);
        SET v_replace = COALESCE(v_replace, 30.00);

        IF NEW.ReturnCondition = 'Lost' THEN
            INSERT INTO Fine (RentalId, LibraryUserId, FineType, Amount, DateAssessed, Notes)
            VALUES (NEW.RentalId, NEW.LibraryUserId, 'Lost', v_replace, NEW.ReturnedDate,
                    CONCAT('Replacement cost for lost ', v_type));
        ELSE
            SET v_days_late = DATEDIFF(NEW.ReturnedDate, NEW.EndDate);

            IF v_days_late > 0 THEN
                INSERT INTO Fine (RentalId, LibraryUserId, FineType, Amount, DaysLate, DateAssessed, Notes)
                VALUES (NEW.RentalId, NEW.LibraryUserId, 'Overdue',
                        LEAST(v_days_late * v_rate, v_cap), v_days_late, NEW.ReturnedDate,
                        CONCAT(v_days_late, ' day(s) late at $', v_rate, '/day'));
            END IF;

            IF NEW.ReturnCondition = 'Damaged' THEN
                INSERT INTO Fine (RentalId, LibraryUserId, FineType, Amount, DateAssessed, Notes)
                VALUES (NEW.RentalId, NEW.LibraryUserId, 'Damaged', v_damage, NEW.ReturnedDate,
                        CONCAT('Damage fee for ', v_type));
            END IF;
        END IF;
    END IF;
END$$

CREATE TRIGGER `trg_rental_return_maintenance` AFTER UPDATE ON `Rentals` FOR EACH ROW BEGIN
    DECLARE v_type          VARCHAR(50);
    DECLARE v_inspect_every INT;
    DECLARE v_completed     INT;
    DECLARE v_recent_damage INT;
    DECLARE v_other_active  INT;

    IF OLD.IsReturned = b'0' AND NEW.IsReturned = b'1' THEN

        SELECT Type INTO v_type FROM Inventory WHERE Id = NEW.ItemId;
        SELECT InspectionEvery INTO v_inspect_every FROM FinePolicy WHERE ItemType = v_type;

        SELECT COUNT(*) INTO v_other_active
          FROM Rentals
         WHERE ItemId = NEW.ItemId AND IsReturned = b'0' AND RentalId <> NEW.RentalId;

        IF NEW.ReturnCondition = 'Lost' THEN
            UPDATE Inventory SET Status = 'Lost' WHERE Id = NEW.ItemId;

        ELSEIF NEW.ReturnCondition = 'Damaged' THEN
            INSERT INTO MaintenanceRequest (ItemId, RentalId, RequestType, Reason, CreatedAt)
            VALUES (NEW.ItemId, NEW.RentalId, 'Damage Repair', 'Returned damaged', COALESCE(NEW.ReturnedDate, NOW()));

            UPDATE Inventory SET Status = 'Under Repair' WHERE Id = NEW.ItemId;

            SELECT COUNT(*) INTO v_recent_damage
              FROM MaintenanceRequest
             WHERE ItemId = NEW.ItemId
               AND RequestType = 'Damage Repair'
               AND CreatedAt >= COALESCE(NEW.ReturnedDate, NOW()) - INTERVAL 180 DAY;

            IF v_recent_damage >= 3 THEN
                INSERT INTO MaintenanceRequest (ItemId, RentalId, RequestType, Reason, CreatedAt)
                VALUES (NEW.ItemId, NEW.RentalId, 'Replacement Review',
                        CONCAT(v_recent_damage, ' damage repairs in the last 180 days'),
                        COALESCE(NEW.ReturnedDate, NOW()));
            END IF;

        ELSE
            SELECT COUNT(*) INTO v_completed
              FROM Rentals
             WHERE ItemId = NEW.ItemId AND IsReturned = b'1';

            IF v_inspect_every IS NOT NULL AND v_inspect_every > 0
               AND v_completed > 0 AND v_completed % v_inspect_every = 0 THEN
                INSERT INTO MaintenanceRequest (ItemId, RentalId, RequestType, Reason, CreatedAt)
                VALUES (NEW.ItemId, NEW.RentalId, 'Routine Inspection',
                        CONCAT('Routine inspection after ', v_completed, ' rentals'),
                        COALESCE(NEW.ReturnedDate, NOW()));

                UPDATE Inventory SET Status = 'Under Repair' WHERE Id = NEW.ItemId;

            ELSEIF v_other_active = 0 THEN
                UPDATE Inventory
                   SET Status = 'Available'
                 WHERE Id = NEW.ItemId
                   AND NOT EXISTS (SELECT 1 FROM MaintenanceRequest m
                                    WHERE m.ItemId = NEW.ItemId
                                      AND m.Status IN ('Open', 'In Progress'));
            END IF;
        END IF;
    END IF;
END$$

CREATE TRIGGER `trg_rental_return_hold` AFTER UPDATE ON `Rentals` FOR EACH ROW BEGIN
    DECLARE v_status   VARCHAR(50);
    DECLARE v_name     VARCHAR(255);
    DECLARE v_type     VARCHAR(50);
    DECLARE v_ready    INT DEFAULT 0;
    DECLARE v_hold     VARCHAR(6) DEFAULT NULL;

    IF OLD.IsReturned = b'0' AND NEW.IsReturned = b'1' THEN

        SELECT Status, Name, Type INTO v_status, v_name, v_type
          FROM Inventory WHERE Id = NEW.ItemId;

        -- Copy already set aside for someone? Then nothing to do.
        SELECT COUNT(*) INTO v_ready
          FROM Hold WHERE ItemId = NEW.ItemId AND Status = 'Ready';

        IF v_status = 'Available' AND v_ready = 0 THEN
            -- Longest-waiting member for this title
            SET v_hold = (
                SELECT h.Hold_ID
                  FROM Hold h
                  JOIN Inventory i ON i.Id = h.ItemId
                 WHERE h.Status = 'Pending'
                   AND i.Name = v_name
                   AND i.Type = v_type
                 ORDER BY h.Request_Datetime
                 LIMIT 1);

            IF v_hold IS NOT NULL THEN
                UPDATE Hold
                   SET Status = 'Ready',
                       ItemId = NEW.ItemId,
                       Ready_Date = NOW(),
                       Pickup_Deadline = TIMESTAMP(DATE(NOW()) + INTERVAL 2 DAY, '23:59:00')
                 WHERE Hold_ID = v_hold;
            END IF;
        END IF;
    END IF;
END$$

CREATE TRIGGER `ValidateRoomReservation` BEFORE INSERT ON `Room_reservation` FOR EACH ROW BEGIN
    DECLARE room_capacity INT DEFAULT NULL;
    DECLARE room_active TINYINT DEFAULT NULL;
    DECLARE conflicting_reservations INT DEFAULT 0;

    SELECT capacity, active
      INTO room_capacity, room_active
      FROM `Room`
     WHERE room_id = NEW.room_id;

    IF room_capacity IS NULL THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Reservation rejected: room does not exist';
    END IF;

    IF room_active IS NULL OR room_active <> 1 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Reservation rejected: room is inactive';
    END IF;

    IF NEW.start_time IS NULL
       OR NEW.end_time IS NULL
       OR NEW.end_time <= NEW.start_time THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Reservation rejected: invalid reservation times';
    END IF;

	IF NEW.num_of_people IS NULL
	   OR NEW.num_of_people < 1 THEN
	    SIGNAL SQLSTATE '45000'
	        SET MESSAGE_TEXT = 'Reservation rejected: group must contain at least 1 person';
	END IF;

    IF NEW.num_of_people > room_capacity THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Reservation rejected: group exceeds room capacity';
    END IF;

    SELECT COUNT(*)
      INTO conflicting_reservations
      FROM `Room_reservation`
     WHERE room_id = NEW.room_id
       AND status <> 'Cancelled'
       AND start_time < NEW.end_time
       AND end_time > NEW.start_time;

    IF conflicting_reservations > 0 THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Reservation rejected: room is already booked during this time';
    END IF;
END$$

CREATE TRIGGER `trg_room_reservation_setup` AFTER INSERT ON `Room_reservation` FOR EACH ROW BEGIN
    INSERT INTO `Room_setup_task`
        (
            reservation_id,
            room_id,
            task_description,
            status,
            created_at
        )
    VALUES
        (
            NEW.reservation_id,
            NEW.room_id,
            CONCAT(
                'Prepare room ',
                NEW.room_id,
                ' for reservation of ',
                NEW.num_of_people,
                ' people'
            ),
            'Open',
            NOW()
        );
END$$

CREATE TRIGGER `trg_room_reservation_cancel_setup` AFTER UPDATE ON `Room_reservation` FOR EACH ROW BEGIN
    IF OLD.status <> 'Cancelled'
       AND NEW.status = 'Cancelled' THEN

        UPDATE `Room_setup_task`
           SET status = 'Cancelled'
         WHERE reservation_id = NEW.reservation_id
           AND status = 'Open';
    END IF;
END$$

DELIMITER ;
