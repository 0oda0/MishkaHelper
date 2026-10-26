plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
    id("org.jetbrains.kotlin.plugin.compose")
}

// Version follows the PC app (src/MishkaHelper.csproj); CI passes -PappVersion=X.Y.Z.
val appVersion = (findProperty("appVersion") as String?) ?: "1.0.0"

android {
    namespace = "com.mishka.helper"
    compileSdk = 34
    (findProperty("buildTools") as String?)?.let { buildToolsVersion = it }   // the owner's SDK has 36 (-PbuildTools=36.0.0); CI uses the default

    defaultConfig {
        applicationId = "com.mishka.helper"
        minSdk = 26
        targetSdk = 34
        versionName = appVersion
        versionCode = appVersion.split(".").map { it.toIntOrNull() ?: 0 }.let { (it.getOrElse(0) { 0 } * 10000) + (it.getOrElse(1) { 0 } * 100) + it.getOrElse(2) { 0 } }
    }

    // One release key, so every new APK installs over the old one. In CI it comes from repository secrets;
    // locally a debug key is used if the keystore isn't there.
    signingConfigs {
        create("release") {
            val ks = System.getenv("MISHKA_KEYSTORE")
            if (ks != null && file(ks).exists()) {
                storeFile = file(ks)
                storePassword = System.getenv("MISHKA_KEYSTORE_PASSWORD")
                keyAlias = "mishka"
                keyPassword = System.getenv("MISHKA_KEYSTORE_PASSWORD")
            }
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = if (System.getenv("MISHKA_KEYSTORE") != null) signingConfigs.getByName("release") else signingConfigs.getByName("debug")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions { jvmTarget = "17" }
    buildFeatures { compose = true }
}

dependencies {
    val bom = platform("androidx.compose:compose-bom:2024.12.01")
    implementation(bom)
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.material3:material3")
    implementation("androidx.compose.material:material-icons-extended")
    implementation("androidx.activity:activity-compose:1.9.3")
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.7")
    implementation("com.squareup.okhttp3:okhttp:4.12.0")
}
