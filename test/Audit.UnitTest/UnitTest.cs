﻿using Audit.Core;
using System;
using Moq;
using Audit.Core.Providers;
using Audit.EntityFramework;
using System.Collections.Generic;
using Audit.Core.Extensions;
using System.Diagnostics;
using NUnit.Framework;

namespace Audit.UnitTest
{
    public class UnitTest
    {
        [Test]
        public void Test_DynamicDataProvider()
        {
            int onInsertCount = 0, onReplaceCount = 0, onInsertOrReplaceCount = 0;
            Core.Configuration.Setup()
                .UseDynamicProvider(config => config
                    .OnInsert(ev => onInsertCount++)
                    .OnReplace((obj, ev) => onReplaceCount++)
                    .OnInsertAndReplace(ev => onInsertOrReplaceCount++));

            var scope = AuditScope.Create("et1", null, EventCreationPolicy.Manual);
            scope.Save();
            scope.SetCustomField("field", "value");
            Assert.AreEqual(1, onInsertCount);
            Assert.AreEqual(0, onReplaceCount);
            Assert.AreEqual(1, onInsertOrReplaceCount);
            scope.Save();
            Assert.AreEqual(1, onInsertCount);
            Assert.AreEqual(1, onReplaceCount);
            Assert.AreEqual(2, onInsertOrReplaceCount);
        }

        [Test]
        public void Test_TypeExtension()
        {
            var s = new List<Dictionary<HashSet<string>, KeyValuePair<int, decimal>>>();
            var fullname = s.GetType().GetFullTypeName();
            Assert.AreEqual("List<Dictionary<HashSet<String>,KeyValuePair<Int32,Decimal>>>", fullname);
        }

        [Test]
        public void Test_EntityFramework_Config_Precedence()
        {
            EntityFramework.Configuration.Setup()
                .ForContext<MyContext>(x => x.AuditEventType("ForContext"))
                .UseOptIn();
            EntityFramework.Configuration.Setup()
                .ForAnyContext(x => x.AuditEventType("ForAnyContext").IncludeEntityObjects(true))
                .UseOptOut();

            var ctx = new MyContext();
            var ctx2 = new AnotherContext();

            Assert.AreEqual("FromAttr", ctx.AuditEventType);
            Assert.AreEqual(true, ctx.IncludeEntityObjects);
            Assert.AreEqual(AuditOptionMode.OptIn, ctx.Mode);

            Assert.AreEqual("ForAnyContext", ctx2.AuditEventType);
            Assert.AreEqual(AuditOptionMode.OptOut, ctx2.Mode);
        }

        [Test]
        public void Test_FluentConfig_FileLog()
        {
            int x = 0;
            Core.Configuration.Setup()
                .UseFileLogProvider(config => config.Directory(@"C:\").FilenamePrefix("prefix"))
                .WithCreationPolicy(EventCreationPolicy.Manual)
                .WithAction(action => action.OnScopeCreated(s => x++));
            var scope = AuditScope.Create("test", null);
            scope.Dispose();
            Assert.AreEqual(typeof(FileDataProvider), Core.Configuration.DataProvider.GetType());
            Assert.AreEqual("prefix", (Core.Configuration.DataProvider as FileDataProvider).FilenamePrefix);
            Assert.AreEqual(@"C:\", (Core.Configuration.DataProvider as FileDataProvider).DirectoryPath);
            Assert.AreEqual(EventCreationPolicy.Manual, Core.Configuration.CreationPolicy);
            Assert.True(Core.Configuration.AuditScopeActions.ContainsKey(ActionType.OnScopeCreated));
            Assert.AreEqual(1, x);
        }
#if NET451
        [Test]
        public void Test_FluentConfig_EventLog()
        {
            Core.Configuration.Setup()
                .UseEventLogProvider(config => config.LogName("LogName").SourcePath("SourcePath").MachineName("MachineName"))
                .WithCreationPolicy(EventCreationPolicy.Manual);
            var scope = AuditScope.Create("test", null);
            scope.Dispose();
            Assert.AreEqual(typeof(EventLogDataProvider), Core.Configuration.DataProvider.GetType());
            Assert.AreEqual("LogName", (Core.Configuration.DataProvider as EventLogDataProvider).LogName);
            Assert.AreEqual("SourcePath", (Core.Configuration.DataProvider as EventLogDataProvider).SourcePath);
            Assert.AreEqual("MachineName", (Core.Configuration.DataProvider as EventLogDataProvider).MachineName);
            Assert.AreEqual(EventCreationPolicy.Manual, Core.Configuration.CreationPolicy);
        }
#endif
        [Test]
        public void Test_StartAndSave()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.Serialize(It.IsAny<string>())).CallBase();

            var eventType = "event type";
            var target = "test";
            AuditScope.CreateAndSave(eventType, new { ExtraField = "extra value" });

            AuditScope.CreateAndSave(eventType, new { Extra1 = new { SubExtra1 = "test1" }, Extra2 = "test2" }, provider.Object);
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Once);
            provider.Verify(p => p.ReplaceEvent(It.IsAny<object>(), It.IsAny<AuditEvent>()), Times.Never);

        }

        [Test]
        public void Test_CustomAction_OnCreating()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.Serialize(It.IsAny<string>())).CallBase();
            
            var eventType = "event type 1";
            var target = "test";
            Core.Configuration.AddCustomAction(ActionType.OnScopeCreated, scope =>
            {
                scope.SetCustomField("custom field", "test");
                if (scope.EventType == eventType)
                {
                    scope.Discard();
                }
            });
            Core.Configuration.AddCustomAction(ActionType.OnEventSaving, scope =>
            {
                Assert.True(false, "This should not be executed");
            });

            AuditEvent ev;
            using (var scope = AuditScope.Create(eventType, () => target, EventCreationPolicy.InsertOnStartInsertOnEnd, provider.Object))
            {
                ev = scope.Event;
            }
            Core.Configuration.ResetCustomActions();
            Assert.True(ev.CustomFields.ContainsKey("custom field"));
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Never);
            provider.Verify(p => p.ReplaceEvent(It.IsAny<object>(), It.IsAny<AuditEvent>()), Times.Never);
        }

        [Test]
        public void Test_CustomAction_OnSaving()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.Serialize(It.IsAny<string>())).CallBase();
            //provider.Setup(p => p.InsertEvent(It.IsAny<AuditEvent>())).Returns((AuditEvent e) => e.Comments);
            var eventType = "event type 1";
            var target = "test";
            var comment = "comment test";
            Core.Configuration.AddCustomAction(ActionType.OnEventSaving, scope =>
            {
                scope.Comment(comment);
            });
            AuditEvent ev;
            using (var scope = AuditScope.Create(eventType, () => target, EventCreationPolicy.Manual, provider.Object))
            {
                ev = scope.Event;
                scope.Save();
            }
            Core.Configuration.ResetCustomActions();
            Assert.True(ev.Comments.Contains(comment));
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Once);
        }

        [Test]
        public void Test_CustomAction_OnCreating_Double()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.Serialize(It.IsAny<string>())).CallBase();
            var eventType = "event type 1";
            var target = "test";
            var key1 = "key1";
            var key2 = "key2";
            Core.Configuration.AddCustomAction(ActionType.OnScopeCreated, scope =>
            {
                scope.SetCustomField(key1, "test");
            });
            Core.Configuration.AddCustomAction(ActionType.OnScopeCreated, scope =>
            {
                scope.Event.CustomFields.Remove(key1);
                scope.SetCustomField(key2, "test");
            });
            AuditEvent ev;
            using (var scope = AuditScope.Create(eventType, () => target, EventCreationPolicy.Manual, provider.Object))
            {
                ev = scope.Event;
            }
            Core.Configuration.ResetCustomActions();
            Assert.False(ev.CustomFields.ContainsKey(key1));
            Assert.True(ev.CustomFields.ContainsKey(key2));
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Never);
            provider.Verify(p => p.ReplaceEvent(It.IsAny<object>(), It.IsAny<AuditEvent>()), Times.Never);
        }

        [Test]
        public void TestSave()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.Serialize(It.IsAny<string>())).CallBase();
            Core.Configuration.DataProvider = provider.Object;
            var target = "initial";
            var eventType = "SomeEvent";
            AuditEvent ev;
            using (var scope = AuditScope.Create(eventType, () => target, EventCreationPolicy.InsertOnEnd))
            {
                ev = scope.Event;
                scope.Comment("test");
                scope.SetCustomField<string>("custom", "value");
                target = "final";
                scope.Save(); // this should do nothing because of the creation policy (this no more true since v4.6.2)
                provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Once);
            }
            Assert.AreEqual(eventType, ev.EventType);
            Assert.True(ev.Comments.Contains("test"));
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Exactly(2));
        }

        [Test]
        public void TestDiscard()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.Serialize(It.IsAny<string>())).CallBase();
            Core.Configuration.DataProvider = provider.Object;
            var target = "initial";
            var eventType = "SomeEvent";
            AuditEvent ev;
            using (var scope = AuditScope.Create(eventType, () => target, EventCreationPolicy.InsertOnEnd))
            {
                ev = scope.Event;
                scope.Comment("test");
                scope.SetCustomField<string>("custom", "value");
                target = "final";
                scope.Discard();
            }
            Assert.AreEqual(eventType, ev.EventType);
            Assert.True(ev.Comments.Contains("test"));
            Assert.Null(ev.Target.SerializedNew);
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Never);
        }

        [Test]
        public void Test_EventCreationPolicy_InsertOnEnd()
        {
            var provider = new Mock<AuditDataProvider>();
            Core.Configuration.DataProvider = provider.Object;
            using (var scope = AuditScope.Create("SomeEvent", () => "target", EventCreationPolicy.InsertOnEnd))
            {
                scope.Comment("test");
                scope.Save(); // this should do nothing because of the creation policy (this is no more true, since v 4.6.2)
            }
            provider.Verify(p => p.ReplaceEvent(It.IsAny<object>(), It.IsAny<AuditEvent>()), Times.Never);
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Exactly(2));
        }

        [Test]
        public void Test_EventCreationPolicy_InsertOnStartReplaceOnEnd()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.InsertEvent(It.IsAny<AuditEvent>())).Returns(() => Guid.NewGuid());
            Core.Configuration.DataProvider = provider.Object;
            using (var scope = AuditScope.Create("SomeEvent", () => "target", EventCreationPolicy.InsertOnStartReplaceOnEnd))
            {
                scope.Comment("test");
            }
            provider.Verify(p => p.ReplaceEvent(It.IsAny<object>(), It.IsAny<AuditEvent>()), Times.Once);
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Once);
        }

        [Test]
        public void Test_EventCreationPolicy_InsertOnStartInsertOnEnd()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.InsertEvent(It.IsAny<AuditEvent>())).Returns(() => Guid.NewGuid());
            Core.Configuration.DataProvider = provider.Object;
            using (var scope = AuditScope.Create("SomeEvent", () => "target", EventCreationPolicy.InsertOnStartInsertOnEnd))
            {
                scope.Comment("test");
            }
            provider.Verify(p => p.ReplaceEvent(It.IsAny<object>(), It.IsAny<AuditEvent>()), Times.Never);
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Exactly(2));
        }

        [Test]
        public void Test_EventCreationPolicy_Manual()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.InsertEvent(It.IsAny<AuditEvent>())).Returns(() => Guid.NewGuid());
            Core.Configuration.DataProvider = provider.Object;
            using (var scope = AuditScope.Create("SomeEvent", () => "target", EventCreationPolicy.Manual))
            {
                scope.Comment("test");
            }
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Never);

            using (var scope = AuditScope.Create("SomeEvent", () => "target", EventCreationPolicy.Manual))
            {
                scope.Comment("test");
                scope.Save();
                scope.Comment("test2");
                scope.Save();
            }
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Once);
            provider.Verify(p => p.ReplaceEvent(It.IsAny<object>(), It.IsAny<AuditEvent>()), Times.Once);
        }

        [Test]
        public void Test_ExtraFields()
        {
            Core.Configuration.DataProvider = new FileDataProvider();
            var scope = AuditScope.Create("SomeEvent", null, new { @class = "class value", DATA = 123 }, EventCreationPolicy.Manual);
            scope.Comment("test");
            var ev = scope.Event;
            scope.Discard();
            Assert.AreEqual("123", ev.CustomFields["DATA"].ToString());
            Assert.AreEqual("class value", ev.CustomFields["class"].ToString());
        }

        [Test]
        public void Test_TwoScopes()
        {
            var provider = new Mock<AuditDataProvider>();
            provider.Setup(p => p.InsertEvent(It.IsAny<AuditEvent>())).Returns(() => Guid.NewGuid());
            Core.Configuration.DataProvider = provider.Object;
            var scope1 = AuditScope.Create("SomeEvent1", null, new { @class = "class value1", DATA = 111 }, EventCreationPolicy.Manual);
            scope1.Save();
            var scope2 = AuditScope.Create("SomeEvent2", null, new { @class = "class value2", DATA = 222 }, EventCreationPolicy.Manual);
            scope2.Save();
            Assert.NotNull(scope1.EventId);
            Assert.NotNull(scope2.EventId);
            Assert.AreNotEqual(scope1.EventId, scope2.EventId);
            provider.Verify(p => p.InsertEvent(It.IsAny<AuditEvent>()), Times.Exactly(2));
        }

        [AuditDbContext(AuditEventType = "FromAttr")]
        public class MyContext : AuditDbContext
        {
            public string AuditEventType { get { return base.AuditEventType; } }
            public bool IncludeEntityObjects { get { return base.IncludeEntityObjects; } }
            public AuditOptionMode Mode { get { return base.Mode; } }
        }
        public class AnotherContext : AuditDbContext
        {
            public string AuditEventType { get { return base.AuditEventType; } }
            public bool IncludeEntityObjects { get { return base.IncludeEntityObjects; } }
            public AuditOptionMode Mode { get { return base.Mode; } }
        }
    }
}
