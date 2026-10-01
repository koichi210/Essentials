using GlobalHook;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventRecorder.Tests
{
    /// <summary>
    /// GlobalHook.MouseHook のテスト。KeyboardHook と同じ理由・同じ方針で、
    /// Start()（システム全体のマウスフックを張る）は絶対に呼ばない。
    /// </summary>
    [TestClass]
    public class MouseHookTests
    {
        [TestMethod]
        public void StartもStopも呼んでいなければフックはしていない()
        {
            Assert.IsFalse(MouseHook.IsHooking);
        }

        [TestMethod]
        public void Pauseを呼ぶとIsPausedがtrueになる()
        {
            MouseHook.Pause();

            Assert.IsTrue(MouseHook.IsPaused);
        }

        [TestMethod]
        public void AddEventで登録したハンドラをRemoveEventで外しても例外にならない()
        {
            MouseHook.HookHandler handler = (ref MouseHook.StateMouse s) => { };

            MouseHook.AddEvent(handler);
            MouseHook.RemoveEvent(handler);
        }

        [TestMethod]
        public void 登録していないハンドラをRemoveEventしても例外にならない()
        {
            MouseHook.HookHandler neverAdded = (ref MouseHook.StateMouse s) => { };

            MouseHook.RemoveEvent(neverAdded);
        }

        [TestMethod]
        public void ClearEventで複数登録した後もすべて例外なく片付く()
        {
            MouseHook.HookHandler a = (ref MouseHook.StateMouse s) => { };
            MouseHook.HookHandler b = (ref MouseHook.StateMouse s) => { };

            MouseHook.AddEvent(a);
            MouseHook.AddEvent(b);

            MouseHook.ClearEvent();
        }
    }
}
