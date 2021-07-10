/*
 
  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.
 
	Copyright (C) 2021 Kelly M. Curtis 

*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenHardwareMonitor.Hardware;

namespace OpenHardwareMonitor.Utilities {
  public interface ILogger {
    void Log();
    TimeSpan LoggingInterval { get; set; }
  }

  public abstract class LoggerBase : ILogger {
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
}
