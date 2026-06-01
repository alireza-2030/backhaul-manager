using Newtonsoft.Json;

while (true)
{
    try
    {
        using var db = new MultiProtocolContext();
        var Clients = db.Client_Traffics.ToList();
        if (Clients == null) Clients = new List<Client_Traffics>();

        if (!File.Exists("LocalDB.json"))
        {
            localDB local = new localDB() { Sec = 10, clients = Clients };
            var LocalD = File.Create("LocalDB.json");
            using (var writer = new StreamWriter(LocalD))
            {
                writer.Write(JsonConvert.SerializeObject(local));
            }
            LocalD.Close();
        }

        localDB localDB = null;
        try
        {
            localDB = JsonConvert.DeserializeObject<localDB>(File.ReadAllText("LocalDB.json"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error reading LocalDB.json: " + ex.Message);
        }
        if (localDB == null)
        {
            localDB = new localDB() { Sec = 10, clients = new List<Client_Traffics>() };
        }
        if (localDB.clients == null)
        {
            localDB.clients = new List<Client_Traffics>();
        }

        List<Client> ALLClients = new List<Client>();

        var inbounds = db.Inbounds.ToList();
        if (inbounds != null)
        {
            foreach (var item in inbounds)
            {
                if (item == null || string.IsNullOrEmpty(item.Settings)) continue;
                try
                {
                    inboundsetting setting = JsonConvert.DeserializeObject<inboundsetting>(item.Settings);
                    if (setting != null && setting.clients != null)
                    {
                        ALLClients.AddRange(setting.clients.Where(x => x != null));
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Parse inbound settings error: " + e.Message);
                }
            }
        }

        List<Client> FinalClients = new List<Client>();
        List<Client_Traffics> FinalClients_Traffic = new List<Client_Traffics>();

        foreach (var client in ALLClients)
        {
            if (client == null || string.IsNullOrEmpty(client.subId)) continue;
            if (!FinalClients.Any(x => x != null && x.subId == client.subId))
            {
                if (ALLClients.Where(x => x != null && x.subId == client.subId).Count() > 1)
                {
                    List<Client> Calculate = ALLClients.Where(x => x != null && x.subId == client.subId).ToList();
                    List<Client_Traffics> Calculate2 = new List<Client_Traffics>();
                    foreach (var client2 in Calculate)
                    {
                        if (client2 == null) continue;
                        var traffic = Clients.Where(x => x != null && x.Email == client2.email).FirstOrDefault();
                        if (traffic != null)
                        {
                            Calculate2.Add(traffic);
                        }
                    }

                    if (Calculate2.Count > 0)
                    {
                        Int64? maxTotalGB = Calculate.Max(x => x.totalGB);
                        Int64? maxTotal = Calculate2.Max(x => x.Total);

                        Int64? maxUP = Calculate2.Max(x => x.Up);
                        Int64? maxDOWN = Calculate2.Max(x => x.Down);
                        Int64? UP = 0;
                        Int64? DOWN = 0;

                        Int64? DateMax = Calculate2.Max(x => x.Expiry_Time);
                        Int64? DateMin = Calculate2.Min(x => x.Expiry_Time);
                        Int64? ExpireTime = 0;
                        if (DateMax > 0)
                        {
                            ExpireTime = DateMax;
                        }
                        else if (DateMin < 0)
                        {
                            ExpireTime = DateMin;
                        }

                        try
                        {
                            foreach (var client2 in Calculate2)
                            {
                                if (client2 == null) continue;
                                if (client2.Up != maxUP)
                                {
                                    var localClient = localDB.clients.Where(x => x != null && x.Email == client2.Email).FirstOrDefault();
                                    if (localClient != null)
                                    {
                                        Int64? oldusage = localClient.Up;
                                        if (client2.Up > oldusage && oldusage != 0 && oldusage != null)
                                        {
                                            UP += client2.Up - oldusage;
                                        }
                                    }
                                }
                                if (client2.Down != maxDOWN)
                                {
                                    var localClient = localDB.clients.Where(x => x != null && x.Email == client2.Email).FirstOrDefault();
                                    if (localClient != null)
                                    {
                                        Int64? oldusage = localClient.Down;
                                        if (client2.Down > oldusage && oldusage != 0 && oldusage != null)
                                        {
                                            DOWN += client2.Down - oldusage;
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("Traffic calculation error: " + e.Message);
                        }

                        foreach (var cal2 in Calculate2)
                        {
                            if (cal2 == null) continue;
                            cal2.Total = maxTotal;
                            cal2.Up = maxUP + UP;
                            cal2.Down = maxDOWN + DOWN;
                            cal2.Expiry_Time = ExpireTime;
                            FinalClients_Traffic.Add(cal2);
                        }
                        foreach (var cal in Calculate)
                        {
                            if (cal == null) continue;
                            cal.totalGB = maxTotalGB;
                            cal.expiryTime = ExpireTime;
                            FinalClients.Add(cal);
                        }
                    }
                }
            }
        }

        if (FinalClients_Traffic.Count > 0)
        {
            db.Client_Traffics.UpdateRange(FinalClients_Traffic);
        }

        List<Inbound> FinalInbounds = new List<Inbound>();
        try
        {
            foreach (var inbound in db.Inbounds)
            {
                if (inbound == null || string.IsNullOrEmpty(inbound.Settings)) continue;
                if (inbound.Protocol == "vmess" || inbound.Protocol == "vless" || inbound.Protocol == "trojan")
                {
                    inboundsetting setting = JsonConvert.DeserializeObject<inboundsetting>(inbound.Settings);
                    if (setting == null) continue;
                    var clis = FinalClients_Traffic.Where(x => x != null && x.Inbound_Id == inbound.Id).ToList();
                    List<Client> addtoInbound = new List<Client>();
                    foreach (var client in clis)
                    {
                        if (client == null) continue;
                        var matchedCli = FinalClients.Where(x => x != null && x.email == client.Email).FirstOrDefault();
                        if (matchedCli != null)
                        {
                            addtoInbound.Add(matchedCli);
                        }
                    }
                    if (addtoInbound.Count > 0)
                    {
                        List<Client> pastclients = new List<Client>();
                        if (setting.clients != null)
                        {
                            foreach (Client client in setting.clients)
                            {
                                if (client == null) continue;
                                if (!addtoInbound.Any(x => x != null && x.email == client.email))
                                {
                                    pastclients.Add(client);
                                }
                            }
                        }
                        pastclients.AddRange(addtoInbound);
                        setting.clients = pastclients;
                        inbound.Settings = JsonConvert.SerializeObject(setting);
                        FinalInbounds.Add(inbound);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Update inbounds error: " + e.Message);
        }

        if (FinalInbounds.Count > 0)
        {
            db.Inbounds.UpdateRange(FinalInbounds);
        }
        db.SaveChanges();

        var client_Traffics = new MultiProtocolContext().Client_Traffics.ToList();
        if (client_Traffics == null) client_Traffics = new List<Client_Traffics>();

        localDB updateLocal = new localDB() { Sec = localDB.Sec, clients = client_Traffics };
        try
        {
            if (File.Exists("LocalDB.json"))
            {
                File.Delete("LocalDB.json");
            }
            var file = File.Create("LocalDB.json");
            using (StreamWriter streamWriter = new StreamWriter(file))
            {
                streamWriter.Write(JsonConvert.SerializeObject(updateLocal));
            }
            file.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine("Error writing LocalDB.json: " + e.Message);
        }

        Console.WriteLine("Synchronization done successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine("Global synchronization iteration error: " + ex.Message);
    }

    Thread.Sleep(25 * 1000);
}
