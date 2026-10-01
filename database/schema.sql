CREATE TABLE Student (
	Student_ID VARCHAR(7) PRIMARY KEY,
	First_Name VARCHAR(30) NOT NULL,
	Last_Name VARCHAR(30) NOT NULL,
	Registration_Date DATETIME NOT NULL,
	AcademicLevel VARCHAR(20),
	DateOfBirth DATE,
	Email VARCHAR(255),
	PhoneNumber VARCHAR(10),
	Street VARCHAR(30),
	City VARCHAR(30),
	State VARCHAR(30),
	ZipCode VARCHAR(5),
	EnrollStatus VARCHAR(50),
	RentalLimit INT,
	WaitlistLimit INT
);
