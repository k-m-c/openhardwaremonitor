﻿/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
	Copyright (C) 2013 Michael Möller <mmoeller@openhardwaremonitor.org>

*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenHardwareMonitor.Hardware;

namespace OpenHardwareMonitor.Utilities {
  public class Logger : LoggerBase, ILogger {

    private const string fileNameFormat =
      "OpenHardwareMonitorLog-{0:yyyy-MM-dd}.csv";

    private DateTime lastLoggedTime = DateTime.MinValue;

    public Logger(IComputer computer) : base(computer) {
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

    private void CreateNewLogFile() {
      IList<ISensor> list = new List<ISensor>();
      SensorVisitor visitor = new SensorVisitor(sensor => {
        list.Add(sensor);
      });
      visitor.VisitComputer(computer);
      sensors = list.ToArray();
      identifiers = sensors.Select(s => s.Identifier.ToString()).ToArray();

      using (StreamWriter writer = new StreamWriter(fileName, false)) {
        writer.Write(",");
        for (int i = 0; i < sensors.Length; i++) {
          writer.Write(sensors[i].Identifier);
          if (i < sensors.Length - 1)
            writer.Write(",");
          else
            writer.WriteLine();
        }

        writer.Write("Time,");
        for (int i = 0; i < sensors.Length; i++) {
          writer.Write('"');
          writer.Write(sensors[i].Name);
          writer.Write('"');
          if (i < sensors.Length - 1)
            writer.Write(",");
          else
            writer.WriteLine();
        }
      }
    }

    public override void Log() {
      var now = DateTime.Now;

      if (lastLoggedTime + LoggingInterval - new TimeSpan(5000000) > now)
        return;

      if (day != now.Date || !File.Exists(fileName)) {
        day = now.Date;
        fileName = GetFileName(day);

        if (!OpenExistingLogFile())
          CreateNewLogFile();
      }

      try {
        using (StreamWriter writer = new StreamWriter(new FileStream(fileName,
          FileMode.Append, FileAccess.Write, FileShare.ReadWrite))) {
          writer.Write(now.ToString("G", CultureInfo.InvariantCulture));
          writer.Write(",");
          for (int i = 0; i < sensors.Length; i++) {
            if (sensors[i] != null) {
              float? value = sensors[i].Value;
              if (value.HasValue)
                writer.Write(
                  value.Value.ToString("R", CultureInfo.InvariantCulture));
            }
            if (i < sensors.Length - 1)
              writer.Write(",");
            else
              writer.WriteLine();
          }
        }
      } catch (IOException) { }

      lastLoggedTime = now;
    }
  }

}
