-- Run against (localdb)\MSSQLLocalDB in SSMS or Azure Data Studio

CREATE DATABASE IotPoc;
GO

USE IotPoc;
GO

CREATE TABLE SensorReadings (
    Id            INT IDENTITY(1,1) PRIMARY KEY,
    SensorId      NVARCHAR(20)   NOT NULL,
    MachineName   NVARCHAR(100)  NOT NULL,
    Zone          NVARCHAR(50)   NOT NULL,
    SensorType    NVARCHAR(50)   NOT NULL,
    Value         DECIMAL(10,2)  NOT NULL,
    Unit          NVARCHAR(20)   NOT NULL,
    Status        NVARCHAR(20)   NOT NULL DEFAULT 'Normal',
    LastUpdated   DATETIME2      NOT NULL DEFAULT GETDATE()
);
GO

INSERT INTO SensorReadings (SensorId, MachineName, Zone, SensorType, Value, Unit, Status) VALUES
('SNS-001', 'Boiler Unit A',        'Zone 1 - Heat',       'Temperature', 72.4,  '°C',  'Normal'),
('SNS-002', 'Boiler Unit A',        'Zone 1 - Heat',       'Pressure',    3.8,   'bar', 'Normal'),
('SNS-003', 'Boiler Unit B',        'Zone 1 - Heat',       'Temperature', 94.1,  '°C',  'Warning'),
('SNS-004', 'Boiler Unit B',        'Zone 1 - Heat',       'Pressure',    5.2,   'bar', 'Critical'),
('SNS-005', 'Conveyor Belt 1',      'Zone 2 - Assembly',   'Vibration',   0.32,  'g',   'Normal'),
('SNS-006', 'Conveyor Belt 1',      'Zone 2 - Assembly',   'RPM',         1450,  'RPM', 'Normal'),
('SNS-007', 'Conveyor Belt 2',      'Zone 2 - Assembly',   'Vibration',   1.87,  'g',   'Warning'),
('SNS-008', 'Conveyor Belt 2',      'Zone 2 - Assembly',   'RPM',         980,   'RPM', 'Warning'),
('SNS-009', 'Compressor Alpha',     'Zone 3 - Pneumatics', 'Pressure',    8.9,   'bar', 'Normal'),
('SNS-010', 'Compressor Alpha',     'Zone 3 - Pneumatics', 'Temperature', 58.2,  '°C',  'Normal'),
('SNS-011', 'Compressor Beta',      'Zone 3 - Pneumatics', 'Pressure',    11.4,  'bar', 'Critical'),
('SNS-012', 'Compressor Beta',      'Zone 3 - Pneumatics', 'Vibration',   2.91,  'g',   'Critical'),
('SNS-013', 'Cooling Tower 1',      'Zone 4 - Cooling',    'Temperature', 28.7,  '°C',  'Normal'),
('SNS-014', 'Cooling Tower 1',      'Zone 4 - Cooling',    'Humidity',    64.0,  '%RH', 'Normal'),
('SNS-015', 'Cooling Tower 2',      'Zone 4 - Cooling',    'Temperature', 41.3,  '°C',  'Warning'),
('SNS-016', 'Cooling Tower 2',      'Zone 4 - Cooling',    'Humidity',    88.5,  '%RH', 'Warning'),
('SNS-017', 'Packaging Machine 1',  'Zone 5 - Packaging',  'RPM',         2200,  'RPM', 'Normal'),
('SNS-018', 'Packaging Machine 1',  'Zone 5 - Packaging',  'Vibration',   0.15,  'g',   'Normal'),
('SNS-019', 'Packaging Machine 2',  'Zone 5 - Packaging',  'RPM',         0,     'RPM', 'Offline'),
('SNS-020', 'Packaging Machine 2',  'Zone 5 - Packaging',  'Temperature', 22.1,  '°C',  'Offline');
GO

ALTER DATABASE IotPoc SET ENABLE_BROKER WITH ROLLBACK IMMEDIATE;
GO
