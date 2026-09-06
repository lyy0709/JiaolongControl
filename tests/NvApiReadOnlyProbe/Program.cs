using System.Text.Json;
using JiaoLongControl.Server.Core.Native;

// Explicit diagnostic entry point. Never initialize the application, PawnIO,
// controllers, automatic profiles, or any native setter.
var handle = NvApiOverclock.GetGpuHandle(0);
var status = NvApiOverclock.ReadClockVfStatus(handle);
var table = NvApiOverclock.ReadTable(handle);
var ranges = NvApiOverclock.GetClockOffsetRange(handle);
var infoBytes = RawCurveRead.Read(handle, 0x507B4B59);
var maskBytes = infoBytes[4..36];
var statusBytes = RawCurveRead.Read(handle, 0x21537AD4, maskBytes);
var controlBytes = RawCurveRead.Read(handle, 0x23F1B133, maskBytes);
Console.WriteLine(JsonSerializer.Serialize(new
{
    RawMask = Enumerable.Range(0, 8).Select(i => BitConverter.ToUInt32(maskBytes, i * 4)),
    RawStatusHeader = Enumerable.Range(0, 16).Select(i => BitConverter.ToUInt32(statusBytes, i * 4)),
    RawControlHeader = Enumerable.Range(0, 16).Select(i => BitConverter.ToUInt32(controlBytes, i * 4)),
    RawPoints = Enumerable.Range(0, 255).Where(i => (maskBytes[i / 8] & (1 << (i % 8))) != 0).Select(i => new
    {
        Id = i,
        StatusWords = Enumerable.Range(0, 7).Select(w => BitConverter.ToUInt32(statusBytes, 64 + i * 28 + w * 4)),
        ControlWords = Enumerable.Range(0, 9).Select(w => BitConverter.ToInt32(controlBytes, 64 + i * 36 + w * 4))
    }),
    StatusVersion = status.Version,
    StatusMask = status.Mask,
    CorePointCount = NvApiOverclock.GetGraphicsCurvePoints(status).Length,
    StatusHeader = status.Reserved,
    TableVersion = table.Version,
    TableMask = table.Mask,
    TableHeader = table.HeaderReserved,
    Range = new { ranges.CoreMinMhz, ranges.CoreMaxMhz },
    Points = status.Entries.Select((p, id) => new
    {
        Id = id,
        Selected = (status.Mask[id >> 5] & (1u << (id & 31))) != 0,
        p.ClockType, p.VoltageMicroV, p.FrequencyKHz,
        OffsetKHz = table.Entries[id].FrequencyOffsetKHz,
        p.Reserved
    })
}, new JsonSerializerOptions { WriteIndented = true }));
