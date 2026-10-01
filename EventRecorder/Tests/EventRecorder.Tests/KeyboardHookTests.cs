using GlobalHook;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EventRecorder.Tests
{
    /// <summary>
    /// GlobalHook.KeyboardHook のテスト。
    ///
    /// ⚠️ Start() は実際に Windows の「システム全体のキーボードフック」を張り、
    /// Stop() するまでこのマシンのキー入力を横取りし続ける。テスト実行中に人がこのマシンで
    /// キーボードを使っている可能性がある以上、絶対に呼び出してはいけない。
    /// そのため Start / Stop / 実際のフックコールバック経路は一切テストしない。
    ///
    /// テストできるのは、OSに触れない範囲のイベント登録の帳簿づけ（AddEvent / RemoveEvent /
    /// ClearEvent）と、静的プロパティの初期状態・単純な setter だけ。
    ///
    /// KeyboardHook は static class で状態も static なので、あるテストで変えた状態は
    /// 同じプロセス内の他のテストにも残る。Start/Stop を一切呼ばないため IsHooking は
    /// このテストクラスの中では常に false のまま、という前提で書いている。
    /// </summary>
    [TestClass]
    public class KeyboardHookTests
    {
        [TestMethod]
        public void StartもStopも呼んでいなければフックはしていない()
        {
            Assert.IsFalse(KeyboardHook.IsHooking);
        }

        [TestMethod]
        public void Pauseを呼ぶとIsPausedがtrueになる()
        {
            // Start を呼ばずに Pause だけを呼ぶのはコード上安全（フラグを立てるだけ）。
            KeyboardHook.Pause();

            Assert.IsTrue(KeyboardHook.IsPaused);
        }

        [TestMethod]
        public void AddEventで登録したハンドラをRemoveEventで外しても例外にならない()
        {
            KeyboardHook.HookHandler handler = (ref KeyboardHook.StateKeyboard s) => { };

            KeyboardHook.AddEvent(handler);
            KeyboardHook.RemoveEvent(handler);

            // 例外が飛ばないことを確認する。内部の Events リストは非公開なので、
            // 「登録・解除の手順が落ちずに完走する」ことだけを見る。
        }

        [TestMethod]
        public void 登録していないハンドラをRemoveEventしても例外にならない()
        {
            KeyboardHook.HookHandler neverAdded = (ref KeyboardHook.StateKeyboard s) => { };

            KeyboardHook.RemoveEvent(neverAdded);
        }

        [TestMethod]
        public void ClearEventで複数登録した後もすべて例外なく片付く()
        {
            KeyboardHook.HookHandler a = (ref KeyboardHook.StateKeyboard s) => { };
            KeyboardHook.HookHandler b = (ref KeyboardHook.StateKeyboard s) => { };

            KeyboardHook.AddEvent(a);
            KeyboardHook.AddEvent(b);

            KeyboardHook.ClearEvent();
        }

        [TestMethod]
        public void 何も登録していない状態でClearEventしても例外にならない()
        {
            KeyboardHook.ClearEvent();
            KeyboardHook.ClearEvent(); // 2回呼んでも安全なはず
        }
    }
}
