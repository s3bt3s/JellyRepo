using System.Net;
using System.Text.Json;
using Jellyfin.Plugin.SimklWatched.API;
using Jellyfin.Plugin.SimklWatched.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MediaBrowser.Model.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using System.Reflection;

int count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
var transport = new Transport();
var api = new SimklApi(NullLogger<SimklApi>.Instance, transport);
var movie = new BaseItemDto { OriginalTitle="Test film", ProductionYear=2020, ProviderIds=new Dictionary<string,string> { ["Imdb"]="tt1234567" } };
transport.Body="{\"added\":{\"movies\":1},\"not_found\":{\"movies\":[],\"shows\":[],\"episodes\":[]}}";
Check(await api.SendManualWatched(movie,false,"test-token"),"movie accepted");
using (var json=JsonDocument.Parse(transport.Request!)) Check(json.RootElement.GetProperty("movies")[0].GetProperty("ids").GetProperty("imdb").GetString()=="tt1234567","movie identifier");
Check(transport.Url!.EndsWith("/sync/history"),"history endpoint only");
movie.SeriesName="Test series";movie.ParentIndexNumber=2;movie.IndexNumber=3;
transport.Body="{\"added\":{\"episodes\":1},\"not_found\":{\"movies\":[],\"shows\":[],\"episodes\":[]}}";
Check(await api.SendManualWatched(movie,true,"test-token"),"episode accepted");
using (var json=JsonDocument.Parse(transport.Request!)) { var show=json.RootElement.GetProperty("shows")[0]; Check(show.GetProperty("seasons")[0].GetProperty("number").GetInt32()==2 && show.GetProperty("seasons")[0].GetProperty("episodes")[0].GetProperty("number").GetInt32()==3,"series season episode payload"); }
transport.Body="{\"added\":{},\"not_found\":{\"movies\":[{}]}}";
Check(!await api.SendManualWatched(movie,false,"test-token"),"unmatched is not success");
transport.Status=HttpStatusCode.TooManyRequests;
try { await api.SendManualWatched(movie,false,"test-token"); throw new Exception("HTTP error ignored"); } catch(HttpRequestException) { Check(true,"HTTP errors propagated"); }
var filter=typeof(WatchedWorker).GetMethod("ShouldQueue",BindingFlags.Static|BindingFlags.NonPublic)!;
bool Eligible(UserDataSaveReason reason,bool played,MediaBrowser.Controller.Entities.BaseItem item) => (bool)filter.Invoke(null,new object[]{new UserDataSaveEventArgs { SaveReason=reason,UserData=new(){Key="test",Played=played},Item=item }})!;
Check(Eligible(UserDataSaveReason.TogglePlayed,true,new Movie()),"manual movie mark accepted");
Check(Eligible(UserDataSaveReason.TogglePlayed,true,new Episode()),"manual episode mark accepted");
Check(!Eligible(UserDataSaveReason.TogglePlayed,false,new Movie()),"unwatched ignored");
Check(!Eligible(UserDataSaveReason.PlaybackProgress,true,new Movie()),"progress ignored even already watched");
Check(!Eligible(UserDataSaveReason.PlaybackFinished,true,new Movie()),"automatic completion ignored");
Check(!Eligible(UserDataSaveReason.TogglePlayed,true,new Series()),"series container ignored");
var stateDir=Path.Combine(Path.GetTempPath(),"simkl-watched-test-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(stateDir);
try
{
 var paths=DispatchProxy.Create<MediaBrowser.Common.Configuration.IApplicationPaths,Stub>();
 ((Stub)(object)paths).Value=stateDir;
 var users=DispatchProxy.Create<IUserDataManager,Stub>();
 var worker=new WatchedWorker(users,null!,api,paths,NullLogger<WatchedWorker>.Instance);
 var stateField=typeof(WatchedWorker).GetField("_deliveries",BindingFlags.Instance|BindingFlags.NonPublic)!;
 var state=(Dictionary<string,WatchedWorker.Delivery>)stateField.GetValue(worker)!;
 state["pending"]=new(){UserId=Guid.NewGuid(),ItemId=Guid.NewGuid(),Attempts=2};
 state["sent"]=new(){UserId=Guid.NewGuid(),ItemId=Guid.NewGuid(),Sent=true};
 typeof(WatchedWorker).GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(worker,null);
 var restored=new WatchedWorker(users,null!,api,paths,NullLogger<WatchedWorker>.Instance);
 using var cancelled=new CancellationTokenSource();cancelled.Cancel();
 await restored.StartAsync(cancelled.Token);
 var loaded=(Dictionary<string,WatchedWorker.Delivery>)stateField.GetValue(restored)!;
 Check(loaded["pending"].Attempts==2 && !loaded["pending"].Sent,"pending survives worker restart");
 Check(!loaded.ContainsKey("sent"),"legacy confirmations removed on restart");
 Check(!File.ReadAllText(Path.Combine(stateDir,"SimklWatched.deliveries.json")).Contains("\"sent\""),"migration persisted to disk");
 var queue=typeof(WatchedWorker).GetMethod("QueueDelivery",BindingFlags.Instance|BindingFlags.NonPublic)!;
 var result=typeof(WatchedWorker).GetMethod("RecordResult",BindingFlags.Instance|BindingFlags.NonPublic)!;
 var userId=Guid.NewGuid();var itemId=Guid.NewGuid();var key=$"{userId:N}:{itemId:N}";
 queue.Invoke(restored,new object[]{userId,itemId});
 var original=loaded[key];
 queue.Invoke(restored,new object[]{userId,itemId});
 Check(!ReferenceEquals(original,loaded[key]),"new click replaces pending request");
 var newest=loaded[key];
 result.Invoke(restored,new object[]{key,original,true});
 Check(ReferenceEquals(newest,loaded[key]),"old success cannot erase new click during HTTP call");
 result.Invoke(restored,new object[]{key,original,false});
 Check(loaded[key].Attempts==0 && loaded[key].NextAttempt<=DateTime.UtcNow,"old failure cannot delay new click");
 original=newest;
 result.Invoke(restored,new object[]{key,original,false});
 Check(loaded[key].Attempts==1 && loaded[key].NextAttempt>DateTime.UtcNow,"failure retained for retry");
 result.Invoke(restored,new object[]{key,original,true});
 Check(!loaded.ContainsKey(key),"success removes pending entry");
 var disk=JsonSerializer.Deserialize<Dictionary<string,WatchedWorker.Delivery>>(File.ReadAllText(Path.Combine(stateDir,"SimklWatched.deliveries.json")))!;
 Check(!disk.ContainsKey(key),"success removal persisted");
 queue.Invoke(restored,new object[]{userId,itemId});
 Check(loaded.ContainsKey(key) && !ReferenceEquals(original,loaded[key]),"new manual mark allowed after success");
 var waiting=loaded[key];
 result.Invoke(restored,new object[]{key,waiting,false});
 queue.Invoke(restored,new object[]{userId,itemId});
 Check(loaded[key].Attempts==0 && loaded[key].NextAttempt<=DateTime.UtcNow,"explicit click bypasses retry backoff");
 var persisted=JsonSerializer.Deserialize<Dictionary<string,WatchedWorker.Delivery>>(File.ReadAllText(Path.Combine(stateDir,"SimklWatched.deliveries.json")))!;
 Check(persisted[key].NextAttempt<=DateTime.UtcNow,"fresh click immediately eligible after restart");
 Check(!File.ReadAllText(Path.Combine(stateDir,"SimklWatched.deliveries.json")).Contains("test-token"),"queue has no token");
 await restored.StopAsync(CancellationToken.None);
 restored.Dispose();worker.Dispose();
}
finally { Directory.Delete(stateDir,true); }
Console.WriteLine($"{count} checks passed");

sealed class Transport : HttpMessageHandler,IHttpClientFactory
{
 public string Body="{}"; public string? Request; public string? Url; public HttpStatusCode Status=HttpStatusCode.OK;
 public HttpClient CreateClient(string name) => new(this,false);
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
 { Request=await request.Content!.ReadAsStringAsync(cancellationToken);Url=request.RequestUri!.ToString();return new(Status){Content=new StringContent(Body)}; }
}

public class Stub : DispatchProxy
{
 public string? Value;
 protected override object? Invoke(MethodInfo? method,object?[]? args) => method!.Name=="get_PluginConfigurationsPath" ? Value : null;
}
