using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SecretaryGadget.Tests
{
    /// <summary>1時間ごとの交代で次に出てくる秘書さん(今の秘書さん以外からランダム)。</summary>
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
        public void Next_NeverRepeatsCurrent()
        {
            var random = new Random(1);
            List<SkinInfo> skins = Skins("blue", "brown", "red");
            for (int i = 0; i < 200; i++)
            {
                Assert.AreNotEqual("brown", SkinCatalog.Next(skins, "brown", random)!.Id);
            }
        }

        [TestMethod]
        public void Next_CurrentMatchIsCaseInsensitive()
        {
            var random = new Random(2);
            List<SkinInfo> skins = Skins("blue", "brown", "red");
            for (int i = 0; i < 200; i++)
            {
                Assert.AreNotEqual("red", SkinCatalog.Next(skins, "RED", random)!.Id);
            }
        }

        [TestMethod]
        public void Next_PicksEveryOtherSkinEventually()
        {
            var random = new Random(3);
            List<SkinInfo> skins = Skins("blue", "brown", "red", "green");
            var seen = new HashSet<string>();
            for (int i = 0; i < 200; i++)
            {
                seen.Add(SkinCatalog.Next(skins, "blue", random)!.Id);
            }
            CollectionAssert.AreEquivalent(new[] { "brown", "red", "green" }, new List<string>(seen));
        }

        [TestMethod]
        public void Next_TwoSkinsAlternate()
        {
            Assert.AreEqual("brown", SkinCatalog.Next(Skins("blue", "brown"), "blue", new Random(4))!.Id);
        }

        [TestMethod]
        public void Next_UnknownCurrentPicksFromAll()
        {
            var random = new Random(5);
            List<SkinInfo> skins = Skins("blue", "brown");
            var seen = new HashSet<string>();
            for (int i = 0; i < 100; i++)
            {
                seen.Add(SkinCatalog.Next(skins, "builtin", random)!.Id);
            }
            CollectionAssert.AreEquivalent(new[] { "blue", "brown" }, new List<string>(seen));
        }

        [TestMethod]
        public void Next_NullWhenFewerThanTwo()
        {
            Assert.IsNull(SkinCatalog.Next(Skins("blue"), "blue", new Random(6)));
            Assert.IsNull(SkinCatalog.Next(Skins(), null, new Random(7)));
        }
    }
}
