using System;
using System.Security.Cryptography;
using System.Text;
using CFileStream = Crestron.SimplSharp.CrestronIO.FileStream;
using CFileMode = Crestron.SimplSharp.CrestronIO.FileMode;
using CFileAccess = Crestron.SimplSharp.CrestronIO.FileAccess;
using CFileShare = Crestron.SimplSharp.CrestronIO.FileShare;
namespace MAV.ProductionRegressionController
{
 internal static class FileHash
 {
  public static string Sha256(string path)
  {
   using (CFileStream stream = new CFileStream(LocalPaths.Normalize(path), CFileMode.Open, CFileAccess.Read, CFileShare.Read))
   using (SHA256 sha = SHA256.Create())
   {
    byte[] buffer = new byte[32768]; int n;
    while ((n=stream.Read(buffer,0,buffer.Length))>0) sha.TransformBlock(buffer,0,n,buffer,0);
    sha.TransformFinalBlock(new byte[0],0,0);
    byte[] hash=sha.Hash; StringBuilder b=new StringBuilder(hash.Length*2);
    for(int i=0;i<hash.Length;i++) b.Append(hash[i].ToString("x2"));
    return b.ToString().ToUpperInvariant();
   }
  }
 }
}
