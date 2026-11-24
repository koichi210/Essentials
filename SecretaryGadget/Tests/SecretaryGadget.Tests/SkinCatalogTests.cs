using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SecretaryGadget.Tests
{
    /// <summary>1時間ごとの交代で次に出てくる秘書さん。</summary>
    [TestClass]
    public class SkinCatalogTests
    {
        private static List<SkinInfo> Skins(params string[] ids)
        {
            var list = new List<SkinInfo>();
            foreach (string id in ids)
            {
                list.Add(new SkinInfo(id, id, "dummy", null));
            }
            return list;
        }

        [TestMethod]
        public void Next_ReturnsFollowingSkin()
        {
            Assert.AreEqual("brown", SkinCatalog.Next(Skins("blue", "brown", "red"), "blue")!.Id);
        }

        [TestMethod]
        public void Next_WrapsToFirstAfterLast()
        {
            Assert.AreEqual("blue", SkinCatalog.Next(Skins("blue", "brown", "red"), "RED")!.Id);
        }

        [TestMethod]
        public void Next_UnknownCurrentStartsFromFirst()
        {
            Assert.AreEqual("blue", SkinCatalog.Next(Skins("blue", "brown"), "builtin")!.Id);
        }

        [TestMethod]
        public void Next_NullWhenFewerThanTwo()
        {
            Assert.IsNull(SkinCatalog.Next(Skins("blue"), "blue"));
            Assert.IsNull(SkinCatalog.Next(Skins(), null));
        }
    }
}
