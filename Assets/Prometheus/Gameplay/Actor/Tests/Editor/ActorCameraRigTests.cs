using NUnit.Framework;
using UnityEngine;

namespace Xuan.Prometheus.Actor.Tests
{
    /// <summary>验证独立 CameraRig 的层级提升、Pawn 销毁隔离、共享根相机复制和 AudioListener 独占生命周期。</summary>
    public sealed class ActorCameraRigTests
    {
        /// <summary>保存测试创建的 CameraDirectorSystem，确保失败路径也能释放独立 Rig。</summary>
        private CameraDirectorSystem director;

        /// <summary>保存测试创建的运行时根对象。</summary>
        private GameObject runtimeRootObject;

        /// <summary>保存测试创建的 Pawn 根对象。</summary>
        private GameObject pawnObject;

        /// <summary>保存测试创建的外部监听器对象。</summary>
        private GameObject externalListenerObject;

        /// <summary>保存测试运行期间动态创建的第二个外部监听器对象。</summary>
        private GameObject dynamicListenerObject;

        /// <summary>保存测试创建的镜头目标对象。</summary>
        private GameObject subjectObject;

        /// <summary>保存测试创建的替换镜头目标对象。</summary>
        private GameObject replacementSubjectObject;

        /// <summary>保存测试创建的镜头跟随配置。</summary>
        private CameraFollowProfile profile;

        /// <summary>每个测试结束后幂等释放系统和全部 Unity 对象，避免监听器状态泄漏到其他测试。</summary>
        [TearDown]
        public void TearDown()
        {
            director?.Dispose();
            director = null;
            if (pawnObject != null) Object.DestroyImmediate(pawnObject);
            if (runtimeRootObject != null) Object.DestroyImmediate(runtimeRootObject);
            if (externalListenerObject != null) Object.DestroyImmediate(externalListenerObject);
            if (dynamicListenerObject != null) Object.DestroyImmediate(dynamicListenerObject);
            if (subjectObject != null) Object.DestroyImmediate(subjectObject);
            if (replacementSubjectObject != null) Object.DestroyImmediate(replacementSubjectObject);
            if (profile != null) Object.DestroyImmediate(profile);
        }

        /// <summary>验证专用相机对象脱离 Pawn 后保持世界姿态，并且 Pawn 销毁不会连带销毁 Rig。</summary>
        [Test]
        public void AdoptCameraRig_DetachesDedicatedCameraAndSurvivesPawnDestruction()
        {
            Camera sourceCamera = CreateDedicatedPawnCamera(out Vector3 worldPosition, out Quaternion worldRotation);
            director = new CameraDirectorSystem();
            CameraRigComponent rig = director.AdoptCameraRig(sourceCamera, runtimeRootObject.transform, pawnObject.transform);
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.OutputCamera, Is.SameAs(sourceCamera));
            Assert.That(rig.transform.parent, Is.SameAs(runtimeRootObject.transform));
            Assert.That(rig.name, Is.EqualTo("ClientCameraRig"));
            Assert.That(Vector3.Distance(rig.transform.position, worldPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(rig.transform.rotation, worldRotation), Is.LessThan(0.0001f));
            Object.DestroyImmediate(pawnObject);
            pawnObject = null;
            Assert.That(sourceCamera, Is.Not.Null);
            Assert.That(director.OutputCamera, Is.SameAs(sourceCamera));
        }

        /// <summary>验证相机与 Pawn 共用 GameObject 时复制独立输出而不提升整个 Pawn，并禁用来源组件。</summary>
        [Test]
        public void AdoptCameraRig_SharedPawnRootCameraCreatesIndependentCopy()
        {
            runtimeRootObject = new GameObject("ActorCameraRigTests.RuntimeRoot");
            pawnObject = new GameObject("ActorCameraRigTests.Pawn");
            pawnObject.transform.SetParent(runtimeRootObject.transform, false);
            Camera sourceCamera = pawnObject.AddComponent<Camera>();
            AudioListener sourceListener = pawnObject.AddComponent<AudioListener>();
            director = new CameraDirectorSystem();
            CameraRigComponent rig = director.AdoptCameraRig(sourceCamera, runtimeRootObject.transform, pawnObject.transform);
            Assert.That(rig.OutputCamera, Is.Not.SameAs(sourceCamera));
            Assert.That(rig.transform.parent, Is.SameAs(runtimeRootObject.transform));
            Assert.That(pawnObject.transform.parent, Is.SameAs(runtimeRootObject.transform));
            Assert.That(sourceCamera.enabled, Is.False);
            Assert.That(sourceListener.enabled, Is.False);
            Object.DestroyImmediate(pawnObject);
            pawnObject = null;
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.OutputCamera, Is.Not.Null);
        }

        /// <summary>验证接管时压制全部已有监听器，并在 Rig 释放后恢复外部监听器。</summary>
        [Test]
        public void CameraRig_ExclusivelyOwnsAudioListenerAndRestoresExternalListenersOnDispose()
        {
            Camera sourceCamera = CreateDedicatedPawnCamera(out _, out _);
            externalListenerObject = new GameObject("ActorCameraRigTests.ExternalListener");
            AudioListener externalListener = externalListenerObject.AddComponent<AudioListener>();
            dynamicListenerObject = new GameObject("ActorCameraRigTests.SecondExternalListener");
            AudioListener secondExternalListener = dynamicListenerObject.AddComponent<AudioListener>();
            director = new CameraDirectorSystem();
            CameraRigComponent rig = director.AdoptCameraRig(sourceCamera, runtimeRootObject.transform, pawnObject.transform);
            Assert.That(rig.AudioListener.enabled, Is.True);
            Assert.That(externalListener.enabled, Is.False);
            Assert.That(secondExternalListener.enabled, Is.False);
            director.Dispose();
            director = null;
            Assert.That(externalListener.enabled, Is.True);
            Assert.That(secondExternalListener.enabled, Is.True);
            Assert.That(sourceCamera == null, Is.True);
        }

        /// <summary>验证基础目标销毁后迟更新安全停留在最后姿态，独立 Rig 不受目标生命周期影响。</summary>
        [Test]
        public void CameraRig_DestroyedSubjectKeepsRigAliveWithoutLateTickException()
        {
            Camera sourceCamera = CreateDedicatedPawnCamera(out _, out _);
            subjectObject = new GameObject("ActorCameraRigTests.Subject");
            CameraSubject subject = subjectObject.AddComponent<CameraSubject>();
            profile = ScriptableObject.CreateInstance<CameraFollowProfile>();
            director = new CameraDirectorSystem();
            CameraRigComponent rig = director.AdoptCameraRig(sourceCamera, runtimeRootObject.transform, pawnObject.transform);
            director.SetBaseTarget(subject, profile, true);
            director.LateTick(0f);
            Object.DestroyImmediate(subjectObject);
            subjectObject = null;
            Assert.DoesNotThrow(() => director.LateTick(1f / 60f));
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.OutputCamera, Is.SameAs(sourceCamera));
            Object.DestroyImmediate(profile);
            profile = null;
        }

        /// <summary>验证切换基础观察目标不会替换或重新挂回 CameraRig，并且旧 Pawn 销毁后继续跟随新目标。</summary>
        [Test]
        public void CameraRig_SwitchingSubjectKeepsSameIndependentOutputCamera()
        {
            Camera sourceCamera = CreateDedicatedPawnCamera(out _, out _);
            CameraSubject firstSubject = pawnObject.AddComponent<CameraSubject>();
            replacementSubjectObject = new GameObject("ActorCameraRigTests.ReplacementSubject");
            replacementSubjectObject.transform.position = new Vector3(30f, 0f, 0f);
            CameraSubject replacementSubject = replacementSubjectObject.AddComponent<CameraSubject>();
            profile = ScriptableObject.CreateInstance<CameraFollowProfile>();
            director = new CameraDirectorSystem();
            CameraRigComponent rig = director.AdoptCameraRig(sourceCamera, runtimeRootObject.transform, pawnObject.transform);
            director.SetBaseTarget(firstSubject, profile, true);
            director.LateTick(0f);
            director.SetBaseTarget(replacementSubject, profile, true);
            director.LateTick(0f);
            Vector3 expectedReplacementPosition = replacementSubject.FollowAnchor.TransformPoint(profile.LocalOffset);
            Assert.That(rig.OutputCamera, Is.SameAs(sourceCamera));
            Assert.That(rig.transform.parent, Is.SameAs(runtimeRootObject.transform));
            Assert.That(Vector3.Distance(sourceCamera.transform.position, expectedReplacementPosition), Is.LessThan(0.0001f));
            Object.DestroyImmediate(pawnObject);
            pawnObject = null;
            Assert.DoesNotThrow(() => director.LateTick(1f / 60f));
            Assert.That(director.ActiveSubject, Is.SameAs(replacementSubject));
        }

        /// <summary>创建一个位于 Pawn 专用子节点上的旧式 Camera 与 AudioListener，并返回接管前世界姿态。</summary>
        private Camera CreateDedicatedPawnCamera(out Vector3 worldPosition, out Quaternion worldRotation)
        {
            runtimeRootObject = new GameObject("ActorCameraRigTests.RuntimeRoot");
            pawnObject = new GameObject("ActorCameraRigTests.Pawn");
            pawnObject.transform.SetParent(runtimeRootObject.transform, false);
            pawnObject.transform.SetPositionAndRotation(new Vector3(8f, 1f, -3f), Quaternion.Euler(0f, 30f, 0f));
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(pawnObject.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 3f, -4f);
            cameraObject.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            worldPosition = cameraObject.transform.position;
            worldRotation = cameraObject.transform.rotation;
            return camera;
        }
    }
}
