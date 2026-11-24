-- Invented seed data (no real client data). Inserts only into empty tables, so reruns do nothing.
-- DaysAgo is relative to today, so "last 30 days" style questions always find rows.
SET QUOTED_IDENTIFIER ON;
GO

USE SafeNlToSql;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Certificates)
BEGIN
    INSERT INTO dbo.Certificates
        (CertificateNumber, EquipmentName, EquipmentType, Metric, CalibrationDate, IsCompliant, ClientName)
    SELECT v.CertificateNumber, v.EquipmentName, v.EquipmentType, v.Metric,
           DATEADD(day, -v.DaysAgo, CAST(GETDATE() AS date)), v.IsCompliant, v.ClientName
    FROM (VALUES
        -- Fridges (Temperature)
        (N'CAL-2025-0001', N'Fridge FR-200', N'Fridge', N'Temperature',   3, 1, N'Northwind Labs'),
        (N'CAL-2025-0002', N'Fridge FR-200', N'Fridge', N'Temperature', 190, 1, N'Northwind Labs'),
        (N'CAL-2025-0003', N'Fridge FR-210', N'Fridge', N'Temperature',  21, 0, N'Contoso Catering'),
        (N'CAL-2025-0004', N'Fridge FR-210', N'Fridge', N'Temperature', 205, 1, N'Contoso Catering'),
        (N'CAL-2025-0005', N'Fridge FR-300', N'Fridge', N'Temperature',  58, 1, N'Fabrikam Foods'),
        (N'CAL-2025-0006', N'Fridge FR-300', N'Fridge', N'Temperature', 330, 0, N'Fabrikam Foods'),
        (N'CAL-2025-0007', N'Fridge FR-310', N'Fridge', N'Temperature', 112, 1, N'Adventure Clinics'),
        (N'CAL-2025-0008', N'Fridge FR-320', N'Fridge', N'Temperature', 447, 1, N'Adventure Clinics'),
        (N'CAL-2025-0009', N'Fridge FR-400', N'Fridge', N'Temperature',  14, 0, N'Tailspin Pharma'),
        (N'CAL-2025-0010', N'Fridge FR-400', N'Fridge', N'Temperature', 260, 1, N'Tailspin Pharma'),
        (N'CAL-2025-0011', N'Fridge FR-410', N'Fridge', N'Temperature', 505, 1, N'Wingtip Bakery'),
        (N'CAL-2025-0012', N'Fridge FR-420', N'Fridge', N'Temperature',  77, 1, N'Wingtip Bakery'),
        -- Microwaves (Frequency)
        (N'CAL-2025-0013', N'Microwave MW-10', N'Microwave', N'Frequency',   5, 0, N'Contoso Catering'),
        (N'CAL-2025-0014', N'Microwave MW-10', N'Microwave', N'Frequency', 175, 1, N'Contoso Catering'),
        (N'CAL-2025-0015', N'Microwave MW-12', N'Microwave', N'Frequency',  12, 0, N'Fabrikam Foods'),
        (N'CAL-2025-0016', N'Microwave MW-12', N'Microwave', N'Frequency', 240, 0, N'Fabrikam Foods'),
        (N'CAL-2025-0017', N'Microwave MW-20', N'Microwave', N'Frequency',  27, 1, N'Northwind Labs'),
        (N'CAL-2025-0018', N'Microwave MW-20', N'Microwave', N'Frequency', 365, 1, N'Northwind Labs'),
        (N'CAL-2025-0019', N'Microwave MW-30', N'Microwave', N'Frequency',  49, 0, N'Tailspin Pharma'),
        (N'CAL-2025-0020', N'Microwave MW-30', N'Microwave', N'Frequency', 420, 1, N'Tailspin Pharma'),
        (N'CAL-2025-0021', N'Microwave MW-40', N'Microwave', N'Frequency',  95, 1, N'Wingtip Bakery'),
        (N'CAL-2025-0022', N'Microwave MW-40', N'Microwave', N'Frequency', 530, 0, N'Wingtip Bakery'),
        (N'CAL-2025-0023', N'Microwave MW-50', N'Microwave', N'Frequency',  29, 1, N'Adventure Clinics'),
        (N'CAL-2025-0024', N'Microwave MW-60', N'Microwave', N'Frequency', 150, 0, N'Adventure Clinics'),
        -- Microphones (AcousticLevel, Linearity)
        (N'CAL-2025-0025', N'Microphone MIC-1', N'Microphone', N'AcousticLevel',   8, 1, N'Litware Studios'),
        (N'CAL-2025-0026', N'Microphone MIC-1', N'Microphone', N'Linearity',      8, 1, N'Litware Studios'),
        (N'CAL-2025-0027', N'Microphone MIC-2', N'Microphone', N'AcousticLevel',  40, 0, N'Litware Studios'),
        (N'CAL-2025-0028', N'Microphone MIC-2', N'Microphone', N'Linearity',     40, 1, N'Litware Studios'),
        (N'CAL-2025-0029', N'Microphone MIC-3', N'Microphone', N'AcousticLevel', 130, 1, N'Proseware Radio'),
        (N'CAL-2025-0030', N'Microphone MIC-3', N'Microphone', N'Linearity',    130, 0, N'Proseware Radio'),
        (N'CAL-2025-0031', N'Microphone MIC-4', N'Microphone', N'AcousticLevel', 300, 1, N'Proseware Radio'),
        (N'CAL-2025-0032', N'Microphone MIC-5', N'Microphone', N'Linearity',    385, 1, N'Northwind Labs'),
        (N'CAL-2025-0033', N'Microphone MIC-6', N'Microphone', N'AcousticLevel', 470, 0, N'Northwind Labs'),
        (N'CAL-2025-0034', N'Microphone MIC-7', N'Microphone', N'Linearity',     65, 1, N'Adventure Clinics'),
        -- Speakers (AcousticLevel, Linearity)
        (N'CAL-2025-0035', N'Speaker SP-100', N'Speaker', N'AcousticLevel',  1, 1, N'Litware Studios'),
        (N'CAL-2025-0036', N'Speaker SP-100', N'Speaker', N'Linearity',      1, 0, N'Litware Studios'),
        (N'CAL-2025-0037', N'Speaker SP-110', N'Speaker', N'AcousticLevel', 33, 1, N'Proseware Radio'),
        (N'CAL-2025-0038', N'Speaker SP-110', N'Speaker', N'Linearity',     33, 1, N'Proseware Radio'),
        (N'CAL-2025-0039', N'Speaker SP-200', N'Speaker', N'AcousticLevel', 88, 0, N'Contoso Catering'),
        (N'CAL-2025-0040', N'Speaker SP-200', N'Speaker', N'Linearity',    215, 1, N'Contoso Catering'),
        (N'CAL-2025-0041', N'Speaker SP-210', N'Speaker', N'AcousticLevel', 275, 1, N'Fabrikam Foods'),
        (N'CAL-2025-0042', N'Speaker SP-220', N'Speaker', N'Linearity',    340, 0, N'Fabrikam Foods'),
        (N'CAL-2025-0043', N'Speaker SP-300', N'Speaker', N'AcousticLevel', 410, 1, N'Wingtip Bakery'),
        (N'CAL-2025-0044', N'Speaker SP-310', N'Speaker', N'Linearity',    540, 1, N'Wingtip Bakery'),
        -- Mixed extras
        (N'CAL-2025-0045', N'Fridge FR-500',     N'Fridge',     N'Temperature',   18, 1, N'Tailspin Pharma'),
        (N'CAL-2025-0046', N'Microwave MW-70',   N'Microwave',  N'Frequency',    120, 1, N'Tailspin Pharma'),
        (N'CAL-2025-0047', N'Microphone MIC-8',  N'Microphone', N'AcousticLevel', 22, 0, N'Proseware Radio'),
        (N'CAL-2025-0048', N'Speaker SP-400',    N'Speaker',    N'Linearity',    160, 0, N'Litware Studios'),
        (N'CAL-2025-0049', N'Fridge FR-510',     N'Fridge',     N'Temperature',  295, 0, N'Adventure Clinics'),
        (N'CAL-2025-0050', N'Microwave MW-80',   N'Microwave',  N'Frequency',    230, 1, N'Northwind Labs')
    ) AS v(CertificateNumber, EquipmentName, EquipmentType, Metric, DaysAgo, IsCompliant, ClientName);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Clients)
BEGIN
    INSERT INTO dbo.Clients (Name, ContactEmail)
    VALUES (N'Northwind Labs',    N'lab@northwind.invalid'),
           (N'Contoso Catering',  N'kitchen@contoso.invalid'),
           (N'Fabrikam Foods',    N'qa@fabrikam.invalid'),
           (N'Tailspin Pharma',   N'quality@tailspin.invalid'),
           (N'Litware Studios',   N'audio@litware.invalid');
END
GO
