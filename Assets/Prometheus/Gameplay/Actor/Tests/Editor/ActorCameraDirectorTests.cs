using NUnit.Framework;
using UnityEngine;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>验证 CameraDirectorSystem 的基础目标、请求抢占、稳定优先级和句柄清理。</summary>
    public sealed class ActorCameraDirectorTests
    {
        /// <summary>保存测试创建的镜头对象。</summary>
        private GameObject cameraObject;

        /// <summary>保存测试创建的第一个 Subject 对象。</summary>
        private GameObject firstSubjectObject;

        /// <summary>保存测试创建的第二个 Subject 对象。</summary>
        private GameObject secondSubjectObject;

        /// <summary>保存测试创建的跟随配置。</summary>
        private CameraFollowProfile profile;

        /// <summary>每个测试结束后销毁全部 Unity 对象，避免测试之间泄漏场景状态。</summary>
        [TearDown]
        public void TearDown()
        {
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
            if (firstSubjectObject != null) Object.DestroyImmediate(firstSubjectObject);
            if (secondSubjectObject != null) Object.DestroyImmediate(secondSubjectObject);
            if (profile != null) Object.DestroyImmediate(profile);
        }

        /// <summary>验证高优先级请求抢占基础目标，释放后自动回退基础目标。</summary>
        [Test]
        public void HigherPriorityRequest_PreemptsBaseTargetAndReleaseRestoresBase()
        {
            CameraDirectorSystem director = CreateDirector(out Camera camera, out CameraSubject first, out CameraSubject second);
            director.SetBaseTarget(first, profile, true);
            director.LateTick(0f);
            Assert.That(director.ActiveSubject, Is.SameAs(first));
            Vector3 expectedBasePosition = first.FollowAnchor.TransformPoint(profile.LocalOffset);
            Assert.That(camera.transform.position, Is.EqualTo(expectedBasePosition));
            CameraRequestHandle handle = director.PushRequest(new CameraRequest(10, second, profile, 100, true));
            director.LateTick(0f);
            Assert.That(director.ActiveSubject, Is.SameAs(second));
            Vector3 expectedOverridePosition = second.FollowAnchor.TransformPoint(profile.LocalOffset);
            Assert.That(camera.transform.position, Is.EqualTo(expectedOverridePosition));
            Assert.That(director.ReleaseRequest(handle), Is.True);
            Assert.That(director.ReleaseRequest(handle), Is.False);
            director.LateTick(10f);
            Assert.That(director.ActiveSubject, Is.SameAs(first));
            director.Dispose();
        }

        /// <summary>验证相同优先级镜头请求由更早提交者稳定获胜。</summary>
        [Test]
        public void EqualPriorityRequests_EarlierRequestWinsDeterministically()
        {
            CameraDirectorSystem director = CreateDirector(out _, out CameraSubject first, out CameraSubject second);
            director.PushRequest(new CameraRequest(1, first, profile, 5));
            director.PushRequest(new CameraRequest(2, second, profile, 5));
            Assert.That(director.ActiveSubject, Is.SameAs(first));
            director.Dispose();
        }

        /// <summary>验证失活的高优先级 Subject 不会阻止系统回退到有效基础目标。</summary>
        [Test]
        public void DisabledRequestSubject_FallsBackToBaseTarget()
        {
            CameraDirectorSystem director = CreateDirector(out _, out CameraSubject first, out CameraSubject second);
            director.SetBaseTarget(first, profile);
            director.PushRequest(new CameraRequest(2, second, profile, 100));
            second.enabled = false;
            Assert.That(director.ActiveSubject, Is.SameAs(first));
            director.Dispose();
        }

        /// <summary>创建包含一个 Camera、两个 Subject 和共享配置的完整测试夹具。</summary>
        private CameraDirectorSystem CreateDirector(out Camera camera, out CameraSubject first, out CameraSubject second)
        {
            cameraObject = new GameObject("ActorCameraDirectorTests.Camera");
            camera = cameraObject.AddComponent<Camera>();
            firstSubjectObject = new GameObject("ActorCameraDirectorTests.FirstSubject");
            secondSubjectObject = new GameObject("ActorCameraDirectorTests.SecondSubject");
            firstSubjectObject.transform.position = new Vector3(10f, 0f, 0f);
            secondSubjectObject.transform.position = new Vector3(20f, 0f, 0f);
            first = firstSubjectObject.AddComponent<CameraSubject>();
            second = secondSubjectObject.AddComponent<CameraSubject>();
            profile = ScriptableObject.CreateInstance<CameraFollowProfile>();
            return new CameraDirectorSystem(camera);
        }
    }
}
