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
using System.Runtime.Serialization.Json;
using System.Runtime.Serialization;

namespace OpenHardwareMonitor.Utilities {

  public class InfluxConfig {
    public string Url { get; set; }
    public int Port { get; set; }
    public string Db { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
  }

  public class InfluxLogger : LoggerBase {
    private string configFileName = @"influx.conf";

    private InfluxConfig _config;

    private InfluxConfig GetConfig() {
      var config = new InfluxConfig() {
        Url = "localhost",
        Port = 8086,
        Db = "openhwmon",
        Username = "",
        Password = ""
      }; //default values

      if (!File.Exists(configFileName)) {
        var ms = new MemoryStream();
        var ser = new DataContractJsonSerializer(typeof(InfluxConfig));
        ser.WriteObject(ms, config);
        byte[] data = ms.ToArray();
        ms.Close();
        var json = Encoding.UTF8.GetString(data, 0, data.Length);

        try {
          File.WriteAllText(configFileName, json);
        } finally {

        }
      } else {
        try {
          var json = File.ReadAllText(configFileName);
          DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(InfluxConfig));
          var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
          config = ser.ReadObject(ms) as InfluxConfig;
          ms.Close();
        }
        catch(Exception ex) {
          return config;
        }
      }

      return config;
    }

    public InfluxLogger(IComputer computer) : base(computer){
    }

    async public override void Log() {

      _config = GetConfig();

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

      var computer_name = Environment.MachineName.Replace(' ', '_').Replace('-', '_');
      try {
        StringBuilder sb = new StringBuilder();

        sb.Append("motherboard,computer=");
        sb.Append(computer_name);
        sb.Append(",sensor=name");

        IHardware mb = computer.Hardware.SingleOrDefault(x => x.HardwareType == HardwareType.Mainboard);
        var mb_name = mb.Name.Replace(" ", "_").Replace("-", "_").Replace("#", "_");

        sb.Append(",device=");
        sb.Append(mb_name);
        sb.Append(",device_id=");
        sb.Append(computer_name + "_");
        sb.Append(mb_name);
        sb.Append(" ");
        sb.Append("value=0");
        sb.Append("\n");

        float? powerTotal = 0.0f;

        for (int i = 0; i < sensors.Length; i++) {
          if (sensors[i] != null) {

            if (sensors[i].SensorType == SensorType.Power) {
              powerTotal += sensors[i].Value.HasValue ? sensors[i].Value : 0;
            }

            var id = sensors[i].Identifier.ToString();
            id = id.Split('/')[1];
            sb.Append(id);
            sb.Append(",computer=");
            sb.Append(computer_name);

            sb.Append(@",sensor=");
            var sensor = sensors[i].Identifier.ToString();
            var breakdown = sensor.Split('/');  //   /cpu/0/temperature/0
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

        sb.Append($"power,computer={computer_name} total={powerTotal.Value}\n");

        var postData = sb.ToString();
        StringContent data = new StringContent(postData, Encoding.UTF8, "text/plain");
        var url = $"http://{_config.Url}:{_config.Port}/write?db={_config.Db}&source={computer_name}";

        if (!string.IsNullOrEmpty(_config.Username)) {
          if (!string.IsNullOrEmpty(_config.Password)) {
            url += $"&u={_config.Username}&p={_config.Password}";
          }
        }

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
