/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2020-2026 Zongsoft Corporation <http://www.zongsoft.com>
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;

namespace Zongsoft.Tools.Packager.Migration;

partial class Migrator
{
	public sealed class AmazonS3(Func<MigrationPlan.Step, IAmazonS3> factory = null) : Migrator
	{
		#region 成员字段
		private readonly Func<MigrationPlan.Step, IAmazonS3> _factory = factory ?? CreateClient;
		#endregion

		#region 公共方法
		public override async Task MigrateAsync(MigrationPlan.Step task, MigrationContext context, CancellationToken cancellation = default)
		{
			using var client = _factory(task);
			Directory.CreateDirectory(context.StateDirectory);

			foreach(var bucket in task.Buckets)
			{
				var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(task.Parameters.Get("Server") + "\n" + bucket.Name)));
				var journal = Path.Combine(context.StateDirectory, "bucket-" + key + ".pending");
				var exists = await Exists(client, bucket.Name, cancellation);

				if(exists && !File.Exists(journal))
				{
					context.Log(string.Format(Properties.Resources.BucketExists, task.Id, bucket.Name));
					continue;
				}

				if(!exists)
				{
					await File.WriteAllTextAsync(journal, "creating", cancellation);

					try
					{
						await client.PutBucketAsync(new PutBucketRequest { BucketName = bucket.Name, BucketRegionName = task.Parameters.Get("Region") }, cancellation);
					}
					catch(AmazonS3Exception ex) when(ex.ErrorCode == "BucketAlreadyOwnedByYou")
					{
						File.Delete(journal);
						if(!await Exists(client, bucket.Name, cancellation))
							throw;

						continue;
					}
					catch(AmazonS3Exception ex) when(ex.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
					{
						File.Delete(journal);
						throw;
					}
				}
				await ConfigureAsync(client, bucket, cancellation);

				if(bucket.Public)
				{
					await client.PutBucketPolicyAsync(new PutBucketPolicyRequest { BucketName = bucket.Name, Policy = CreatePolicy(bucket.Name) }, cancellation);
				}

				File.Delete(journal);
				context.Log(string.Format(Properties.Resources.BucketCreated, task.Id, bucket.Name));
			}
		}
		#endregion

		#region 私有方法
		private static async Task ConfigureAsync(IAmazonS3 client, MigrationPlan.Bucket bucket, CancellationToken cancellation)
		{
			if(bucket.Versioning != null)
			{
				await client.PutBucketVersioningAsync(new PutBucketVersioningRequest
				{
					BucketName = bucket.Name,
					VersioningConfig = new S3BucketVersioningConfig
					{
						Status = bucket.Versioning == "enabled" ? VersionStatus.Enabled : VersionStatus.Suspended,
					},
				}, cancellation);
			}

			if(bucket.Encryption != null)
			{
				await client.PutBucketEncryptionAsync(new PutBucketEncryptionRequest
				{
					BucketName = bucket.Name,
					ServerSideEncryptionConfiguration = new ServerSideEncryptionConfiguration
					{
						ServerSideEncryptionRules =
						[
							new ServerSideEncryptionRule
							{
								ServerSideEncryptionByDefault = new ServerSideEncryptionByDefault
								{
									ServerSideEncryptionAlgorithm = bucket.Encryption.Mode == "sse-s3" ? ServerSideEncryptionMethod.AES256 : ServerSideEncryptionMethod.AWSKMS,
									ServerSideEncryptionKeyManagementServiceKeyId = bucket.Encryption.Key,
								},
							},
						],
					},
				}, cancellation);
			}

			if(bucket.Tags is { Count: > 0 })
			{
				await client.PutBucketTaggingAsync(new PutBucketTaggingRequest
				{
					BucketName = bucket.Name,
					TagSet = bucket.Tags.Select(tag => new Tag { Key = tag.Key, Value = tag.Value }).ToList(),
				}, cancellation);
			}
		}

		private static string CreatePolicy(string bucket)
		{
			using var stream = new MemoryStream();
			using(var writer = new Utf8JsonWriter(stream))
			{
				writer.WriteStartObject();
				writer.WriteString("Version", "2012-10-17");
				writer.WriteStartArray("Statement");
				writer.WriteStartObject();
				writer.WriteString("Sid", "ZongsoftPackagerPublicRead");
				writer.WriteString("Effect", "Allow");
				writer.WriteString("Principal", "*");
				writer.WriteString("Action", "s3:GetObject");
				writer.WriteString("Resource", $"arn:aws:s3:::{bucket}/*");
				writer.WriteEndObject();
				writer.WriteEndArray();
				writer.WriteEndObject();
			}

			return Encoding.UTF8.GetString(stream.ToArray());
		}

		private static async Task<bool> Exists(IAmazonS3 client, string name, CancellationToken cancellation)
		{
			try
			{
				await client.HeadBucketAsync(new HeadBucketRequest { BucketName = name }, cancellation);
				return true;
			}
			catch(AmazonS3Exception ex) when(ex.StatusCode == HttpStatusCode.NotFound)
			{
				return false;
			}
		}

		private static IAmazonS3 CreateClient(MigrationPlan.Step task)
		{
			var p = task.Parameters;
			return new AmazonS3Client(new BasicAWSCredentials(p.Get("AccessKey"), p.Get("SecretKey")), new AmazonS3Config
			{
				MaxErrorRetry = 0,
				ForcePathStyle = true,
				ServiceURL = p.Get("Server"),
				AuthenticationRegion = p.Get("Region"),
				Timeout = TimeSpan.FromSeconds(p.Seconds("Timeout", 30)),
			});
		}
		#endregion
	}
}
