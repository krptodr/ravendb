﻿using System.Text;
using Raven.Client.Replication.Messages;
using Raven.Server.ServerWide.Context;
using Sparrow.Json;
using Voron;

namespace Raven.Server.Documents
{
    public class DocumentConflict
    {
        public LazyStringValue LoweredKey;
        public LazyStringValue Key;
        public BlittableJsonReaderObject Doc;
        public long StorageId;
        public ChangeVectorEntry[] ChangeVector;
        public long InsertedEtag; // the etag of the current db when this conflict was added
        
        public static implicit operator DocumentConflict(Document doc)
        {
            return new DocumentConflict
            {
                LoweredKey = doc.LoweredKey,
                Key = doc.Key,
                Doc = doc.Data,
                StorageId = doc.StorageId,
                ChangeVector = doc.ChangeVector
            };
        }

        public static implicit operator DocumentConflict(DocumentTombstone doc)
        {
            return new DocumentConflict
            {
                LoweredKey = doc.LoweredKey,
                Key = doc.Key,
                Doc = null,
                StorageId = doc.StorageId,
                ChangeVector = doc.ChangeVector
            };
        }

    }
}