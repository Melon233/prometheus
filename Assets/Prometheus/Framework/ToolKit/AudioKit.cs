using UnityEngine;
using Xuan.Prometheus.Component;

namespace Xuan.Prometheus
{
    public class AudioKit : MonoSingleton<AudioKit>  // 音频管理器
    {
        private AudioSource audioSource;  // 音频源组件
        protected override void Awake()
        {
            base.Awake();
            audioSource = gameObject.AddComponent<AudioSource>();  // 获取音频源组件
        }
        public void Play(AudioClip clip)  // 播放音频
        {
            audioSource.PlayOneShot(clip);
        }
    }
}