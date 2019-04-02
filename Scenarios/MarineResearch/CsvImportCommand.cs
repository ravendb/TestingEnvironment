using System;
using System.IO;
using System.Net.Http;
using Raven.Client.Http;
using Sparrow.Json;

namespace MarineResearch
{
    public class CsvImportCommand : RavenCommand
    {
        private readonly Stream _stream;
        private readonly string _collection;
        private readonly long _operationId;

        public override bool IsReadRequest => false;

        public CsvImportCommand(Stream stream, string collection, long operationId)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));

            _collection = collection;
            _operationId = operationId;
        }

        public override HttpRequestMessage CreateRequest(JsonOperationContext ctx, ServerNode node, out string url)
        {
            url = $"{node.Url}/databases/{node.Database}/smuggler/import/csv?operationId={_operationId}&collection={_collection}";

            var form = new MultipartFormDataContent
            {
                {new StreamContent(_stream), "file", "name"}
            };

            _stream.Position = 0;

            return new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                Content = form
            };
        }
    }
}