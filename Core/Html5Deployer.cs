using System;
using System.Collections.Generic;
namespace MAV.ProductionRegressionController
{
 public sealed class Html5Deployer
 {
  private readonly ProcessorTransport _transport;
  private readonly ControllerLogger _log;
  public Html5Deployer(ProcessorTransport transport, ControllerLogger log) { _transport=transport; _log=log; }
  public void DeployForPackage(TargetSettings target, MavPackageType type)
  {
   if(type==MavPackageType.VtcFarm) DeploySet(target,"MAV_VTC_FARM_MONITOR_UI","/html/mav-vtc-farm-monitor-ui");
   else if(type==MavPackageType.Room)
   {
    DeploySet(target,"MAV_MONITOR_UI","/html/mav-monitor-ui");
    DeploySet(target,"MAV_SYSTEM_DEFINITION_UI","/html/mav-system-definition-ui");
   }
  }
  private void DeploySet(TargetSettings target,string assetSet,string remoteRoot)
  {
   string root=LocalPaths.RuntimeWebAssets + "/" + assetSet;
   _transport.EnsureDirectory(target,remoteRoot);
   int count=0;
   foreach(KeyValuePair<string,string> asset in TargetWebAssets.FilesFor(assetSet))
   {
    // Embedded manifest is the authority. Write each file with CrestronIO and upload that exact path.
    string relative=asset.Key.Replace('\\','/');
    string local=root + "/" + relative;
    LocalFileIO.WriteAllBytes(local,Convert.FromBase64String(asset.Value));
    _transport.Upload(target,local,remoteRoot + "/" + relative);
    count++;
   }
   if(count==0) throw new InvalidOperationException("Missing embedded HTML5 asset set: " + assetSet);
   _log.Pass("HTML5 site deployed: " + remoteRoot + " (" + count + " files).");
  }
 }
}
