using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Jackett.Mobile.Core.Cloudflare;
using Jackett.Mobile.Core.Models;
using Jackett.Mobile.Core.Services;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Jackett.Mobile.Core.Test
{
    [TestFixture]
    public class MobileHostTests
    {
        // the definitions are copied next to the test assembly by Jackett.Common
        private static string DefinitionsFolder => Path.Combine(TestContext.CurrentContext.TestDirectory, "Definitions");

        [Test]
        public async Task StartsWithAllIndexersAndBuildsSetupForms()
        {
            using var temp = new TempFolder();
            var browser = new FakeBrowser();
            using var host = MobileHost.Start(new MobileHostOptions
            {
                DataFolder = temp.Path,
                BundledDefinitionsFolder = DefinitionsFolder,
                AppVersion = "v1.0.0-test",
                BrowserSolver = browser
            });

            var indexers = host.Setup.GetIndexers();
            var definitionCount = Directory.GetFiles(DefinitionsFolder, "*.yml").Length;

            // every YAML definition plus the C# indexers
            Assert.That(indexers.Count, Is.GreaterThanOrEqualTo(definitionCount));
            Assert.That(indexers.Any(i => i.Id == "redacted"), Is.True, "C# indexer missing");
            Assert.That(indexers.Any(i => i.IsConfigured), Is.False);

            var yamlIndexer = Path.GetFileNameWithoutExtension(Directory.GetFiles(DefinitionsFolder, "*.yml").First());
            var form = await host.Setup.GetFormAsync(yamlIndexer);
            Assert.That(form.Any(f => f.Id == "sitelink"), Is.True);

            var log = File.ReadAllText(Path.Combine(temp.Path, "log.txt"));
            Assert.That(log, Does.Contain("Using HTTP Client: MobileWebClient"));

            Assert.That(host.CustomDefinitionsFolder, Does.StartWith(temp.Path));
            Assert.That(Directory.Exists(host.CustomDefinitionsFolder), Is.True);
        }

        [Test]
        public void CustomDefinitionsOverrideBundledOnes()
        {
            using var temp = new TempFolder();
            var bundled = Path.Combine(temp.Path, "bundle", "Definitions");
            Directory.CreateDirectory(bundled);
            var source = Directory.GetFiles(DefinitionsFolder, "*.yml").First();
            var id = Path.GetFileNameWithoutExtension(source);
            File.Copy(source, Path.Combine(bundled, id + ".yml"));

            var custom = Path.Combine(temp.Path, "data", "Definitions");
            Directory.CreateDirectory(custom);
            var yaml = File.ReadAllText(source);
            var nameLine = yaml.Split('\n').First(l => l.StartsWith("name:"));
            File.WriteAllText(Path.Combine(custom, id + ".yml"), yaml.Replace(nameLine, "name: Custom Copy"));

            using var host = MobileHost.Start(new MobileHostOptions
            {
                DataFolder = Path.Combine(temp.Path, "data"),
                BundledDefinitionsFolder = bundled
            });

            Assert.That(host.Setup.GetIndexer(id).Name, Is.EqualTo("Custom Copy"));
        }

        [Test]
        public void ProtectionRoundTripsAndDetectsTampering()
        {
            var service = new MobileProtectionService(MobileProtectionService.GenerateKey());
            var protectedText = service.Protect("hunter2");

            Assert.That(protectedText, Is.Not.EqualTo("hunter2"));
            Assert.That(service.UnProtect(protectedText), Is.EqualTo("hunter2"));
            Assert.That(service.UnProtect(""), Is.EqualTo(""));

            var other = new MobileProtectionService(MobileProtectionService.GenerateKey());
            Assert.Throws<CryptographicException>(() => other.UnProtect(protectedText));
        }

        [Test]
        public void ParsesAndSubmitsConfigFields()
        {
            var json = JArray.Parse(@"[
                {id:'sitelink', type:'inputstring', name:'Site Link', value:'https://a/'},
                {id:'freeleech', type:'inputbool', name:'Freeleech', value:true},
                {id:'sort', type:'inputselect', name:'Sort', value:'date', options:{date:'Date', size:'Size'}},
                {id:'cats', type:'inputcheckbox', name:'Cats', values:['1'], options:{'1':'One','2':'Two'}},
                {id:'info', type:'displayinfo', name:'Info', value:'<b>hi</b>'},
                {id:'captcha', type:'displayimage', name:'Captcha', value:'data:image/png;base64,AAEC'}
            ]");

            var fields = ConfigField.ParseAll(json);
            Assert.That(fields.Single(f => f.Id == "freeleech").BoolValue, Is.True);
            Assert.That(fields.Single(f => f.Id == "sort").Options.Select(o => o.Key), Is.EqualTo(new[] { "date", "size" }));
            Assert.That(fields.Single(f => f.Id == "captcha").ImageBytes, Is.EqualTo(new byte[] { 0, 1, 2 }));

            fields.Single(f => f.Id == "cats").Values.Add("2");
            var submission = ConfigField.ToSubmission(fields);

            Assert.That(submission.Count, Is.EqualTo(4), "display fields are not submitted");
            Assert.That(submission.Single(t => (string)t["id"] == "freeleech")["value"].Value<bool>(), Is.True);
            Assert.That(submission.Single(t => (string)t["id"] == "cats")["values"].Values<string>(), Is.EqualTo(new[] { "1", "2" }));
        }

        [Test]
        public void RecognisesMagnetDownloadsAndBuildsFileNames()
        {
            Assert.That(DownloadService.StartsWithMagnet(System.Text.Encoding.ASCII.GetBytes("magnet:?xt=urn:btih:abc")), Is.True);
            Assert.That(DownloadService.StartsWithMagnet(new byte[] { 0x64, 0x38 }), Is.False);
            Assert.That(DownloadService.ToFileName("Show: S01/E02 \"720p\""), Is.EqualTo("Show_ S01_E02 _720p_"));
        }

        [Test]
        public void BrowserLoginFillsCookieAndUserAgent()
        {
            var fields = new[]
            {
                new ConfigField { Id = "cookie", Type = ConfigField.TypeString },
                new ConfigField { Id = "user-agent", Type = ConfigField.TypeString }
            };
            var clearance = Responses.Clearance("tracker.example");
            clearance.Cookies.Add(new BrowserCookie { Name = "uid", Value = "42", Domain = "tracker.example" });
            clearance.Cookies.Add(new BrowserCookie { Name = "other", Value = "x", Domain = "elsewhere.example" });

            IndexerSetupService.ApplyBrowserLogin(fields, clearance, "tracker.example", DateTime.UtcNow);

            Assert.That(fields[0].Value, Is.EqualTo("cf_clearance=solved; uid=42"));
            Assert.That(fields[1].Value, Is.EqualTo("Mozilla/5.0 (iPhone) Safari"));
        }
    }
}
