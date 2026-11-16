using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace RoadRush.Editor
{
    /// <summary>Builds a private, empty-scene native test player with no career startup.</summary>
    public static class SplittieProofBuild
    {
        public static void Build()
        {
            string company=PlayerSettings.companyName,product=PlayerSettings.productName;
            string identifier=PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
            int result=1;
            try
            {
                PlayerSettings.companyName="Blackwood QA";PlayerSettings.productName="Splittie QA";
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,"games.blackwood.roadrush.splittieqa");
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene,"Assets/SplittieQA.unity");
                var report=BuildPipeline.BuildPlayer(new[]{"Assets/SplittieQA.unity"},"/private/tmp/SplittieQA.app",BuildTarget.StandaloneOSX,BuildOptions.None);
                if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Splittie QA build failed");
                result=0;
            }
            catch(Exception error){Debug.LogException(error);}
            finally
            {
                PlayerSettings.companyName=company;PlayerSettings.productName=product;
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,identifier);
                AssetDatabase.DeleteAsset("Assets/SplittieQA.unity");AssetDatabase.SaveAssets();EditorApplication.Exit(result);
            }
        }
    }
}
