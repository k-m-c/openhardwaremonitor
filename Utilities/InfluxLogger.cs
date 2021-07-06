/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
	Copyright (C) 2021 Kelly M. Curtis 

*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using OpenHardwareMonitor.Hardware;

public interface ILogger {
  void Log();
  TimeSpan LoggingInterval { get; set; }
}

namespace OpenHardwareMonitor.Utilities {
  public abstract class LoggerBase: ILogger{
    protected readonly IComputer computer;

    protected DateTime day = DateTime.MinValue;
    protected string fileName;
    protected string[] identifiers;
    protected ISensor[] sensors;

    protected DateTime lastLoggedTime = DateTime.MinValue;

    public TimeSpan LoggingInterval { get; set; }

    public LoggerBase(IComputer computer) {
      this.computer = computer;
      this.computer.HardwareAdded += HardwareAdded;
      this.computer.HardwareRemoved += HardwareRemoved;
    }

    private void HardwareRemoved(IHardware hardware) {
      hardware.SensorAdded -= SensorAdded;
      hardware.SensorRemoved -= SensorRemoved;
      foreach (ISensor sensor in hardware.Sensors)
        SensorRemoved(sensor);
      foreach (IHardware subHardware in hardware.SubHardware)
        HardwareRemoved(subHardware);
    }

    private void HardwareAdded(IHardware hardware) {
      foreach (ISensor sensor in hardware.Sensors)
        SensorAdded(sensor);
      hardware.SensorAdded += SensorAdded;
      hardware.SensorRemoved += SensorRemoved;
      foreach (IHardware subHardware in hardware.SubHardware)
        HardwareAdded(subHardware);
    }

    private void SensorAdded(ISensor sensor) {
      if (sensors == null)
        return;

      for (int i = 0; i < sensors.Length; i++) {
        if (sensor.Identifier.ToString() == identifiers[i])
          sensors[i] = sensor;
      }
    }

    private void SensorRemoved(ISensor sensor) {
      if (sensors == null)
        return;

      for (int i = 0; i < sensors.Length; i++) {
        if (sensor == sensors[i])
          sensors[i] = null;
      }
    }

    public abstract void Log();
  }

  public class InfluxLogger : LoggerBase {

    private const string fileNameFormat =
      "klog-{0:yyyy-MM-dd}.csv";

    public InfluxLogger(IComputer computer) : base(computer){
      
    }

    private static string GetFileName(DateTime date) {
      return AppDomain.CurrentDomain.BaseDirectory +
        Path.DirectorySeparatorChar + string.Format(fileNameFormat, date);
    }

    private bool OpenExistingLogFile() {
      if (!File.Exists(fileName))
        return false;

      try {
        String line;
        using (StreamReader reader = new StreamReader(fileName))
          line = reader.ReadLine();

        if (string.IsNullOrEmpty(line))
          return false;

        identifiers = line.Split(',').Skip(1).ToArray();
      } catch {
        identifiers = null;
        return false;
      }

      if (identifiers.Length == 0) {
        identifiers = null;
        return false;
      }

      sensors = new ISensor[identifiers.Length];
      SensorVisitor visitor = new SensorVisitor(sensor => {
        for (int i = 0; i < identifiers.Length; i++)
          if (sensor.Identifier.ToString() == identifiers[i])
            sensors[i] = sensor;
      });
      visitor.VisitComputer(computer);
      return true;
    }

    //private void CreateNewLogFile() {
    //  IList<ISensor> list = new List<ISensor>();
    //  SensorVisitor visitor = new SensorVisitor(sensor => {
    //    list.Add(sensor);
    //  });
    //  visitor.VisitComputer(computer);
    //  sensors = list.ToArray();
    //  identifiers = sensors.Select(s => s.Identifier.ToString()).ToArray();

    //  //using (StreamWriter writer = new StreamWriter(fileName, false)) {
    //  //  writer.Write(",");
    //  //  for (int i = 0; i < sensors.Length; i++) {
    //  //    writer.Write(sensors[i].Identifier);
    //  //    if (i < sensors.Length - 1)
    //  //      writer.Write(",");
    //  //    else
    //  //      writer.WriteLine();
    //  //  }

    //  //  writer.Write("Time,");
    //  //  for (int i = 0; i < sensors.Length; i++) {
    //  //    writer.Write('"');
    //  //    writer.Write(sensors[i].Name);
    //  //    writer.Write('"');
    //  //    if (i < sensors.Length - 1)
    //  //      writer.Write(",");
    //  //    else
    //  //      writer.WriteLine();
    //  //  }
    //  //}
    //}

    async public override void Log() {
      IList<ISensor> list = new List<ISensor>();
      SensorVisitor visitor = new SensorVisitor(sensor => {
        list.Add(sensor);
      });
      visitor.VisitComputer(computer);
      sensors = list.ToArray();
      identifiers = sensors.Select(s => s.Identifier.ToString()).ToArray();
      var now = DateTime.Now;

      if (lastLoggedTime + LoggingInterval - new TimeSpan(5000000) > now)
        return;
      fileName = "log.txt";

      //try {
      //  using (StreamWriter writer = new StreamWriter(new FileStream(fileName,
      //    FileMode.Append, FileAccess.Write, FileShare.ReadWrite))) {
      //    writer.Write(now.ToString("G", CultureInfo.InvariantCulture));
      //    writer.Write(",");
      //    for (int i = 0; i < sensors.Length; i++) {
      //      if (sensors[i] != null) {
      //        float? value = sensors[i].Value;
      //        if (value.HasValue)
      //          writer.Write(
      //            value.Value.ToString("R", CultureInfo.InvariantCulture));
      //      }
      //      if (i < sensors.Length - 1)
      //        writer.Write(",");
      //      else
      //        writer.WriteLine();
      //    }
      //  }
      //} catch (IOException) { }


      var computer_name = Environment.MachineName.Replace(' ', '_').Replace('-', '_');
      try {
        StringBuilder sb = new StringBuilder();

        sb.Append("motherboard,computer=");
        sb.Append(computer_name);
        sb.Append(",sensor=name");
        var mb_name = computer.Hardware[0].Name.Replace(" ", "_").Replace("-", "_").Replace("#", "_");
        sb.Append(",device=");
        sb.Append(mb_name);
        sb.Append(",device_id=");
        sb.Append(computer_name + "_");
        sb.Append(mb_name);
        sb.Append(" ");
        sb.Append("value=0");
        sb.Append("\n");

        for (int i = 0; i < sensors.Length; i++) {
          if (sensors[i] != null) {
            var id = sensors[i].Identifier.ToString();
            id = id.Split('/')[1];
            sb.Append(id);
            sb.Append(",computer=");
            sb.Append(computer_name);
            sb.Append(@",sensor=");
            var sensor = sensors[i].Identifier.ToString();
            var breakdown = sensor.Split('/');
            //   /cpu/0/temperature/0
            int device_number = 0;
            if (int.TryParse(breakdown[2], out device_number)) {
              sensor = breakdown[3];
            } else {
              sensor = breakdown[2];
            }
            sb.Append(sensor);
            sb.Append(@",device=");
            var device_name = sensors[i].Hardware.Name.Replace(' ', '_').Replace('-', '_');
            sb.Append(device_name);
            sb.Append(",device_id=");
            sb.Append(computer_name + "_");
            sb.Append(device_name + "_");
            sb.Append(device_number);
            sb.Append(",sensor_number=");
            var sensor_number = 0;
            int.TryParse(breakdown[breakdown.Length - 1], out sensor_number);
            sb.Append(sensor_number);
            sb.Append(@",openhw_id=");
            sb.Append(sensors[i].Identifier.ToString().Replace('/', '_'));
            sb.Append(@" ");
            sb.Append(sensors[i].Name.Replace(" ", "_").Replace('#', '0'));
            sb.Append("=");
            float? value = sensors[i].Value;
            if (value.HasValue) {
              sb.Append(value.Value.ToString("R", CultureInfo.InvariantCulture));
            } else {
              sb.Append("0");
            }
            sb.Append("\n");
          }
        }

        var postData = sb.ToString();
        StringContent data = new StringContent(postData, Encoding.UTF8, "text/plain");
        var url = "http://MKM-DEV-CORE:8086/write?db=openhwmon&source=" + computer_name;
        var client = new HttpClient();

        var response = await client.PostAsync(url, data);
        if (response.StatusCode == HttpStatusCode.BadRequest) {
          using (StreamWriter writer = new StreamWriter(new FileStream(fileName,
            FileMode.Append, FileAccess.Write, FileShare.ReadWrite))) {
            writer.WriteLine(postData);
          }
        }

        string result = await response.Content.ReadAsStringAsync();

        client.Dispose();

      } catch (Exception ex) {
        try {
          using (StreamWriter writer = new StreamWriter(new FileStream(fileName,
            FileMode.Append, FileAccess.Write, FileShare.ReadWrite))) {
            writer.Write("Error=");
            writer.Write(ex);
            writer.WriteLine();
          }
        } catch (IOException) { }
      }

      lastLoggedTime = now;
    }
  }


}
