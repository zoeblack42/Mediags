﻿using System.IO;
using NongCrypto;

namespace NongFormat
{
    public class Sha256Format : HashesContainer
    {
        public static string[] Names
        { get { return new string[] { "sha256" }; } }

        public override string[] ValidNames
        { get { return Names; } }

        public static Model CreateModel (Stream stream, byte[] hdr, string path)
        {
            if (path.ToLower().EndsWith(".sha256"))
                return new Model (stream, hdr, path);
            return null;
        }


        public new class Model : HashesContainer.Model
        {
            public new readonly Sha256Format Data;

            public Model (Stream stream, byte[] header, string path) : base (path, 32)
            {
                base._data = Data = new Sha256Format (stream, path, HashedModel.Bind);
                Data.Issues = IssueModel.Data;

                ParseHashes();
            }


            public override void CalcHashes (Hashes hashFlags, Validations validationFlags)
            {
                if (Data.Issues.HasFatal)
                    return;

                if ((validationFlags & Validations.SHA256) != 0)
                    ComputeContentHashes (new Sha256Hasher());

                base.CalcHashes (hashFlags, validationFlags);
            }
        }


        private Sha256Format (Stream stream, string path, HashedFile.Vector hashedVector) : base (stream, path, hashedVector)
        {
            this.Validation = Validations.SHA256;
        }
    }
}
