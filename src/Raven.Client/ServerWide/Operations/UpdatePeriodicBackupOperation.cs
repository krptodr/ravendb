﻿using System.Net.Http;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Session;
using Raven.Client.Http;
using Raven.Client.Json;
using Raven.Client.Json.Converters;
using Raven.Client.ServerWide.PeriodicBackup;
using Sparrow.Json;

namespace Raven.Client.ServerWide.Operations
{
    public class UpdatePeriodicBackupOperation : IServerOperation<UpdatePeriodicBackupOperationResult>
    {
        private readonly PeriodicBackupConfiguration _configuration;

        public UpdatePeriodicBackupOperation(PeriodicBackupConfiguration configuration)
        {
            _configuration = configuration;
        }

        public RavenCommand<UpdatePeriodicBackupOperationResult> GetCommand(DocumentConventions conventions, JsonOperationContext ctx)
        {
            return new UpdatePeriodicBackupCommand(_configuration);
        }

        public class UpdatePeriodicBackupCommand : RavenCommand<UpdatePeriodicBackupOperationResult>
        {
            private readonly PeriodicBackupConfiguration _configuration;

            public UpdatePeriodicBackupCommand(PeriodicBackupConfiguration configuration)
            {
                _configuration = configuration;
            }

            public override bool IsReadRequest => false;

            public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
            {
                url = $"{node.Url}/databases/{node.Database}/admin/periodic-backup";

                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    Content = new BlittableJsonContent(stream =>
                    {
                        var config = EntityToBlittable.ConvertEntityToBlittable(_configuration, DocumentConventions.Default, ctx);
                        ctx.Write(stream, config);
                    })
                };

                return request;
            }

            public override void SetResponse(BlittableJsonReaderObject response, bool fromCache)
            {
                if (response == null)
                    ThrowInvalidResponse();

                Result = JsonDeserializationClient.ConfigurePeriodicBackupOperationResult(response);
            }
        }
    }
}
