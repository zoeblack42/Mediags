﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NongIssue;
using NongCrypto;

namespace NongFormat
{
    public enum HashStyle { Undefined, Text, Binary, Media };

    public abstract class HashesContainer : FormatBase
    {
        public class Model : FormatBase.ModelBase
        {
            public readonly HashedFile.Vector.Model HashedModel;
            public new HashesContainer Data => (HashesContainer) _data;
            public HashesHistory.Model HistoryModel { get; protected set; }

            public Model (string rootPath, int hashLength)
            { HashedModel = new HashedFile.Vector.Model (rootPath, hashLength); }


            public void CreateHistory()
            {
                HistoryModel = new HashesHistory.Model();
                Data.History = HistoryModel.Bind;
            }

            public void DestroyHistory()
            {
                Data.History = null;
                HistoryModel = null;
            }


            public void ParseHeaderAndHistory()
            {
                Data.fbs.Position = 0;
                TextReader tr = new StreamReader (Data.fbs, Data.encoding);
                var lx = tr.ReadLine();
                if (lx == null)
                    return;

                var versionRegex = new Regex (@"^; generated by ([a-zA-Z]+) v([0-9]+)\.([0-9]+)\.([0-9]+).*$");
                MatchCollection reMatches = versionRegex.Matches (lx);
                if (reMatches.Count == 1)
                {
                    Data.Generator = reMatches[0].Groups[1].ToString();

                    var isOK = Int32.TryParse (reMatches[0].Groups[2].ToString(), out int major);
                    isOK = Int32.TryParse (reMatches[0].Groups[3].ToString(), out int minor);

                    Data.mediaPosition = lx.Length + 4;
                    Data.MediaCount = 8;

                    lx = tr.ReadLine();
                    if (lx == null || lx.Length < 10)
                    {
                        IssueModel.Add ("File is truncated.", Severity.Fatal);
                        return;
                    }

                    string crcText = lx.Substring (2, 8);

                    isOK = UInt32.TryParse (crcText, System.Globalization.NumberStyles.HexNumber, null, out uint crc);
                    if (! isOK)
                    {
                        IssueModel.Add ("Self-CRC is not valid.", Severity.Fatal);
                        return;
                    }

                    CreateHistory();
                    HistoryModel.SetStoredSelfCRC (crc);

                    string sig = null, action = null;
                    for (;;)
                    {
                        lx = tr.ReadLine();
                        if (lx == null || ! lx.StartsWith (";"))
                            break;

                        if (lx.Length > 24)
                        {
                            string ly = lx.Substring (2);
                            HistoryModel.AddLine (ly);

                            var hre = new Regex (@"^[1-2][0-9][0-9][0-9][0-9][0-9][0-9][0-9] [0-2][0-9]\:[0-5][0-9]\:[0-9][0-9]\: ([^\:]+)\: (.+)");
                            MatchCollection matches = hre.Matches (ly);
                            if (matches.Count < 1)
                                IssueModel.Add ("Cannot find a signature.");
                            else
                            {
                                sig = matches[0].Groups[1].ToString();
                                action = matches[0].Groups[2].ToString();

                                if (action == "proved" || action == "verified")
                                    HistoryModel.SetProver (sig);
                            }
                        }
                    }

                    if (Data.History.Comment.Count == 0)
                    {
                        DestroyHistory();
                        IssueModel.Add ("History comments are missing.");
                    }
                    else
                        HistoryModel.SetLastAction (sig, action);
                }
            }


            protected void ParseHashes()
            {
                Data.fbs.Position = 0;
                TextReader tr = new StreamReader (Data.fbs, Data.encoding);

                for (int line = 1;; ++line)
                {
                    var lx = tr.ReadLine();
                    if (lx == null)
                        return;

                    lx = lx.TrimStart();
                    if (lx.Length == 0 || lx[0] == ';')
                        continue;

                    if (lx.Length < Data.HashedFiles.HashLength*2+3)
                    {
                        IssueModel.Add ("Too short, line " + line + '.', Severity.Fatal);
                        return;
                    }

                    // Try typical format with hash first.
                    var style = HashStyle.Undefined;
                    if (lx[Data.HashedFiles.HashLength*2]==' ')
                    {
                        var styleChar = lx[Data.HashedFiles.HashLength*2+1];
                        if (styleChar==GetStyleChar (HashStyle.Text))
                            style = HashStyle.Text;
                        else if (styleChar==GetStyleChar (HashStyle.Binary))
                            style = HashStyle.Binary;
                        else if (styleChar==GetStyleChar (HashStyle.Media))
                            style = HashStyle.Media;

                        if (style != HashStyle.Undefined)
                        {
                            var hash = ConvertTo.FromHexStringToBytes (lx, 0, Data.HashedFiles.HashLength);
                            if (hash != null)
                            {
                                var targetName = lx.Substring (Data.HashedFiles.HashLength*2 + 2);
                                HashedModel.Add (targetName, hash, style);
                                continue;
                            }
                        }
                    }

                    // Fall back to layout with name followed by hash.
                    if (lx[lx.Length-Data.HashedFiles.HashLength*2-1]==' ')
                    {
                        var hash = ConvertTo.FromHexStringToBytes (lx, lx.Length-Data.HashedFiles.HashLength*2, Data.HashedFiles.HashLength);
                        if (hash != null)
                        {
                            var targetName = lx.Substring (0, lx.Length-Data.HashedFiles.HashLength*2-1);
                            HashedModel.Add (targetName, hash, HashStyle.Binary);
                            continue;
                        }
                    }

                    IssueModel.Add ("Badly formed, line " + line + '.', Severity.Fatal);
                }
            }


            public void WriteFile (string generator, Encoding cp)
            {
                using (var ms = new MemoryStream())
                {
                    var bb = cp.GetBytes ("; generated by " + generator + Environment.NewLine);
                    ms.Write (bb, 0, bb.Length);

                    var crcPosition = (int) ms.Length + 2;

                    if (HistoryModel != null)
                    {
                        bb = cp.GetBytes ("; 00000000 do not modify" + Environment.NewLine);
                        ms.Write (bb, 0, bb.Length);

                        bb = cp.GetBytes (";" + Environment.NewLine);
                        ms.Write (bb, 0, bb.Length);

                        foreach (var historyItem in Data.History.Comment)
                        {
                            var lx = "; " + historyItem + Environment.NewLine;
                            bb = cp.GetBytes (lx);
                            ms.Write (bb, 0, bb.Length);
                        }
                    }

                    bb = cp.GetBytes (";" + Environment.NewLine);
                    ms.Write (bb, 0, bb.Length);

                    foreach (var hashedItem in Data.HashedFiles.Items)
                        if (hashedItem.ActualHash != null)
                        {
                            bb = cp.GetBytes (hashedItem.ActualHashToHex.ToLower() + ' ' + GetStyleChar (hashedItem.Style) + hashedItem.FileName + Environment.NewLine);
                            ms.Write (bb, 0, bb.Length);
                        }

                    if (HistoryModel != null)
                    {
                        // Calculate self-CRC.
                        var hasher = new Crc32rHasher();
                        hasher.Append (ms, 0, crcPosition, crcPosition+8, ms.Length-crcPosition-8);
                        UInt32 crc = BitConverter.ToUInt32 (hasher.GetHashAndReset(), 0);
                        HistoryModel.SetActualSelfCRC (crc);
                        HistoryModel.SetStoredSelfCRC (crc);

                        bb = cp.GetBytes (String.Format ("{0:X8}", Data.History.ActualCRC));
                        ms.Seek (crcPosition, SeekOrigin.Begin);
                        ms.Write (bb, 0, bb.Length);
                        HistoryModel.SetIsDirty (false);
                    }

                    Data.fBuf = ms.ToArray();
                    ms.Seek (0, SeekOrigin.Begin);
                    Data.fbs.Position = 0;
                    ms.CopyTo (Data.fbs);
                    Data.fbs.SetLength (Data.fbs.Position);
                    Data.mediaPosition = crcPosition;
                    ResetFile();

                    for (int ix = 0; ix < Data.HashedFiles.Items.Count; ++ix)
                        HashedModel.SetOldFileName (ix, null);
                }

                Data.NotifyPropertyChanged (null);
            }


            protected void ComputeContentHashes (CryptoHasher hasher, Hashes mediaHash=Hashes.None)
            {
                System.Diagnostics.Debug.Assert (Data.HashedFiles.HashLength == hasher.HashLength);

                for (int index = 0; index < Data.HashedFiles.Items.Count; ++index)
                {
                    HashedFile item = Data.HashedFiles.Items[index];
                    string msg = null;
                    var targetName = Data.HashedFiles.GetPath (index);

                    try
                    {
                        using (var tfs = new FileStream (targetName, FileMode.Open, FileAccess.Read))
                        {
                            HashedModel.SetIsFound (index, true);
                            byte[] hash = null;

                            if (item.Style == HashStyle.Media)
                                if (mediaHash == Hashes.None)
                                    IssueModel.Add ("Unexpected media hash on item " + (index+1) + ".");
                                else
                                {
                                    var hdr = new byte[0x3F];
                                    tfs.Position = 0;
                                    int got = tfs.Read (hdr, 0, hdr.Length);
                                    Mp3Format.Model fmt = Mp3Format.CreateModel (tfs, hdr, targetName);
                                    if (fmt == null)
                                        // Only MP3 supported for now.
                                        IssueModel.Add ("Unexpected file format.");
                                    else
                                    {
                                        fmt.CalcHashes (mediaHash, Validations.None);
                                        fmt.CloseFile();
                                        hash = fmt.Data.MediaSHA1;
                                    }
                                }
                            else
                            {
                                hasher.Append (tfs);
                                hash = hasher.GetHashAndReset();
                            }

                            HashedModel.SetActualHash (index, hash);
                            if (item.IsMatch == false)
                                IssueModel.Add (Data.HasherName + " mismatch on '" + item.FileName + "'.");
                        }
                    }
                    catch (FileNotFoundException ex)
                    { msg = ex.Message.TrimEnd (null); }
                    catch (IOException ex)
                    { msg = ex.Message.TrimEnd (null); }
                    catch (UnauthorizedAccessException ex)
                    { msg = ex.Message.TrimEnd (null); }

                    if (msg != null)
                    {
                        HashedModel.SetIsFound (index, false);
                        IssueModel.Add (msg);
                    }
                }

                string tx = Data.HasherName + " validation of "  + Data.HashedFiles.Items.Count + " file";
                if (Data.HashedFiles.Items.Count != 1)
                    tx += "s";
                if (base.Data.Issues.MaxSeverity < Severity.Error)
                    tx += " successful.";
                else
                    tx += " failed with " + Data.HashedFiles.FoundCount + " found and " + Data.HashedFiles.MatchCount + " matched.";

                IssueModel.Add (tx, Severity.Advisory);
            }


            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (Data.Issues.HasFatal)
                    return;

                if ((hashFlags & Hashes.Intrinsic) != 0 && Data.History != null && Data.History.ActualCRC == null)
                {
                    var hasher = new Crc32rHasher();
                    hasher.Append (Data.fbs, 0, (int) Data.mediaPosition, (int) Data.mediaPosition + 8, Data.FileSize - Data.mediaPosition - 8);
                    byte[] hash = hasher.GetHashAndReset();
                    HistoryModel.SetActualSelfCRC (BitConverter.ToUInt32 (hash, 0));

                    if (Data.History.ActualCRC == Data.History.StoredCRC)
                        IssueModel.Add ("Self-CRC check successful.", Severity.Noise);
                    else
                        IssueModel.Add ("Self-CRC mismatch, file has been modified.");
                }

                base.CalcHashes (hashFlags, validationFlags);
            }
        }


        private static readonly char[] styleChar = new char[] { '?', ' ', '*', ':' };
        public static char GetStyleChar (HashStyle hashStyle) { return styleChar[(int) hashStyle]; }

        public string Generator { get; private set; }
        public HashesHistory History { get; private set; }

        public HashedFile.Vector HashedFiles { get; private set; }
        public Validations Validation { get; protected set; }
        public string HasherName { get { return Validation.ToString(); } }

        private Encoding encoding;

        public HashesContainer (Stream stream, string path, HashedFile.Vector hashedVector, Encoding encoding=null) : base (stream, path)
        {
            this.encoding = encoding ?? LogBuffer.cp1252;
            this.HashedFiles = hashedVector;
        }


        public override void GetDetailsBody (IList<string> report, Granularity scope)
        {
            if (scope > Granularity.Detail)
                return;

            if (report.Count != 0)
                report.Add (String.Empty);

            if (Generator != null)
                report.Add ("Product = " + Generator);

            report.Add (HasherName + " count = " + HashedFiles.Items.Count);

            foreach (HashedFile item in HashedFiles.Items)
                report.Add (item.StoredHashToHex + ' ' + GetStyleChar (item.Style) + item.FileName);
        }
    }
}
