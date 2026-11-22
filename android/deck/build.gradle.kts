plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "app.mydeck"
    compileSdk = 35
    buildToolsVersion = "35.0.0"

    defaultConfig {
        applicationId = "app.mydeck"
        minSdk = 22
        targetSdk = 34
        versionCode = ((System.currentTimeMillis() / 60000L) - 29_440_000L).toInt().coerceAtLeast(2)
        versionName = Regex("<Version>([^<]+)</Version>").find(rootProject.file("../windows/MyDeck.Server/MyDeck.Server.csproj").readText())?.groupValues?.get(1) ?: "dev"
    }

    val keystore = System.getenv("MYDECK_KEYSTORE")?.let { file(it) }?.takeIf { it.exists() }
    signingConfigs {
        if (keystore != null) create("release") {
            storeFile = keystore
            storePassword = System.getenv("MYDECK_STORE_PASSWORD")
            keyAlias = System.getenv("MYDECK_KEY_ALIAS")
            keyPassword = System.getenv("MYDECK_KEY_PASSWORD")
        }
    }
    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.findByName("release") ?: signingConfigs.getByName("debug")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
}
