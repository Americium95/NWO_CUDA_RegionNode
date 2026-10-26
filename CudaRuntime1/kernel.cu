#include "cuda_runtime.h"
#include "device_launch_parameters.h"

#include <stdio.h> 


// ============================================================
// CUDA Kernel
// ============================================================

__global__ void disFilterKernel(
    float* c,
    float4 start,
    const float4* a,
    unsigned int size)
{
    int i = blockIdx.x * blockDim.x + threadIdx.x;

    if (i >= size)
        return;

    if (abs(a[i].x - start.x) + abs(a[i].y - start.y) < 5)
    {
        c[i] =
            abs(a[i].z - start.z +
                (a[i].x - start.x) * 2560)
            +
            abs(a[i].w - start.w -
                (a[i].y - start.y) * 2560);
    }
    else
    {
        c[i] = 100000;
    }
}

// ============================================================
// CUDA 전역 메모리
// ============================================================

float4* dev_user_a = nullptr;
float* dev_user_c = nullptr;

float4* dev_movement_a = nullptr;
float* dev_movement_c = nullptr;


// ============================================================
// User
// ============================================================

cudaError_t userMemCopyWithCuda(
    const float4* a,
    unsigned int arraySize);

cudaError_t userDisFilterWithCuda(
    float* c,
    const float4 start,
    unsigned int size);

int userMemFreeWithCuda();


// ============================================================
// Movement
// ============================================================

cudaError_t movementMemCopyWithCuda(
    const float4* a,
    unsigned int arraySize);

cudaError_t movementDisFilterWithCuda(
    float* c,
    const float4 start,
    unsigned int size);

int movementMemFreeWithCuda();


// ============================================================
// Export - User
// ============================================================

extern "C" __declspec(dllexport)
int cudaUserMemCopy(float4* a, int arraySize)
{
    if (arraySize < 0 || (arraySize > 0 && a == nullptr))
        return (int)cudaErrorInvalidValue;
    if (arraySize == 0)
    {
        userMemFreeWithCuda();
        return (int)cudaSuccess;
    }
    return (int)userMemCopyWithCuda(a, (unsigned int)arraySize);
}

extern "C" __declspec(dllexport)
float* cudaUserDisFilter(
    float* output,
    float4 start,
    int arraySize)
{
    if (arraySize < 0 || (arraySize > 0 && output == nullptr))
        return nullptr;
    if (arraySize == 0)
    {
        userMemFreeWithCuda();
        return output;
    }
    if (userDisFilterWithCuda(output, start, (unsigned int)arraySize) != cudaSuccess)
        return nullptr;

    return output;
}

extern "C" __declspec(dllexport)
int cudaUserMemFree()
{
    return userMemFreeWithCuda();
}


// ============================================================
// Export - Movement
// ============================================================

extern "C" __declspec(dllexport)
int cudaMoveMentMemCopy(float4* a, int arraySize)
{
    if (arraySize < 0 || (arraySize > 0 && a == nullptr))
        return (int)cudaErrorInvalidValue;
    if (arraySize == 0)
    {
        movementMemFreeWithCuda();
        return (int)cudaSuccess;
    }
    return (int)movementMemCopyWithCuda(a, (unsigned int)arraySize);
}

extern "C" __declspec(dllexport)
float* cudaMoveMentDisFilter(
    float* output,
    float4 start,
    int arraySize)
{
    if (arraySize < 0 || (arraySize > 0 && output == nullptr))
        return nullptr;
    if (arraySize == 0)
    {
        movementMemFreeWithCuda();
        return output;
    }
    if (movementDisFilterWithCuda(
        output,
        start,
        (unsigned int)arraySize) != cudaSuccess)
        return nullptr;

    return output;
}

extern "C" __declspec(dllexport)
int cudaMoveMentMemFree()
{
    return movementMemFreeWithCuda();
}


// ============================================================
// User - GPU 메모리 복사
// ============================================================

cudaError_t userMemCopyWithCuda(
    const float4* a,
    unsigned int arraySize)
{
    cudaError_t cudaStatus;

    userMemFreeWithCuda();

    cudaStatus = cudaMalloc(
        (void**)&dev_user_a,
        arraySize * sizeof(float4));

    if (cudaStatus != cudaSuccess)
    {
        fprintf(
            stderr,
            "cudaMalloc dev_user_a failed\n");

        userMemFreeWithCuda();
        return cudaStatus;
    }

    cudaStatus = cudaMemcpy(
        dev_user_a,
        a,
        arraySize * sizeof(float4),
        cudaMemcpyHostToDevice);

    if (cudaStatus != cudaSuccess)
    {
        fprintf(
            stderr,
            "cudaMemcpy dev_user_a failed\n");

        userMemFreeWithCuda();

        return cudaStatus;
    }

    cudaStatus = cudaMalloc(
        (void**)&dev_user_c,
        arraySize * sizeof(float));

    if (cudaStatus != cudaSuccess)
    {
        fprintf(
            stderr,
            "cudaMalloc dev_user_c failed\n");

        userMemFreeWithCuda();
        return cudaStatus;
    }

    return cudaSuccess;
}


// ============================================================
// User - GPU 연산
// ============================================================

cudaError_t userDisFilterWithCuda(
    float* c,
    const float4 start,
    unsigned int size)
{
    if (size == 0)
        return cudaSuccess;
    if (c == nullptr || dev_user_a == nullptr || dev_user_c == nullptr)
        return cudaErrorInvalidValue;

    unsigned int threadsPerBlock = 256;

    unsigned int blocks =
        (size + threadsPerBlock - 1)
        / threadsPerBlock;

    disFilterKernel << <blocks, threadsPerBlock >> > (
        dev_user_c,
        start,
        dev_user_a,
        size);

    cudaError_t cudaStatus =
        cudaGetLastError();

    if (cudaStatus != cudaSuccess)
    {
        fprintf(
            stderr,
            "user kernel launch failed: %s\n",
            cudaGetErrorString(cudaStatus));

        userMemFreeWithCuda();

        return cudaStatus;
    }

    cudaStatus =
        cudaDeviceSynchronize();

    if (cudaStatus != cudaSuccess)
    {
        fprintf(
            stderr,
            "user cudaDeviceSynchronize failed: %s\n",
            cudaGetErrorString(cudaStatus));

        userMemFreeWithCuda();

        return cudaStatus;
    }

    cudaStatus = cudaMemcpy(
        c,
        dev_user_c,
        size * sizeof(float),
        cudaMemcpyDeviceToHost);

    if (cudaStatus != cudaSuccess)
    {
        fprintf(
            stderr,
            "user cudaMemcpy failed: %s\n",
            cudaGetErrorString(cudaStatus));

        userMemFreeWithCuda();

        return cudaStatus;
    }

    return cudaSuccess;
}


// ============================================================
// User - GPU 메모리 해제
// ============================================================

int userMemFreeWithCuda()
{
    if (dev_user_c != nullptr)
    {
        cudaFree(dev_user_c);
        dev_user_c = nullptr;
    }

    if (dev_user_a != nullptr)
    {
        cudaFree(dev_user_a);
        dev_user_a = nullptr;
    }

    return 0;
}


// ============================================================
// Movement - GPU 메모리 복사
// ============================================================

cudaError_t movementMemCopyWithCuda(
    const float4* a,
    unsigned int arraySize)
{
    cudaError_t cudaStatus;

    movementMemFreeWithCuda();

    cudaStatus = cudaMalloc(
        (void**)&dev_movement_a,
        arraySize * sizeof(float4));

    if (cudaStatus != cudaSuccess)
    {
        fprintf(
            stderr,
            "cudaMalloc dev_movement_a failed\n");

        movementMemFreeWithCuda();
        return cudaStatus;
    }

    cudaStatus = cudaMemcpy(
        dev_movement_a,
        a,
        arraySize * sizeof(float4),
        cudaMemcpyHostToDevice);

    if (cudaStatus != cudaSuccess)
    {
        fprintf(
            stderr,
            "cudaMemcpy dev_movement_a failed\n");

        movementMemFreeWithCuda();

        return cudaStatus;
    }

    cudaStatus = cudaMalloc(
        (void**)&dev_movement_c,
        arraySize * sizeof(float));

    if (cudaStatus != cudaSuccess)
    {
        fprintf(
            stderr,
            "cudaMalloc dev_movement_c failed\n");

        movementMemFreeWithCuda();

        return cudaStatus;
    }

    return cudaSuccess;
}


// ============================================================
// Movement - GPU 연산
// ============================================================

cudaError_t movementDisFilterWithCuda(
    float* c,
    const float4 start,
    unsigned int size)
{
    if (size == 0)
        return cudaSuccess;
    if (c == nullptr || dev_movement_a == nullptr || dev_movement_c == nullptr)
        return cudaErrorInvalidValue;

    unsigned int threadsPerBlock = 256;

    unsigned int blocks =
        (size + threadsPerBlock - 1)
        / threadsPerBlock;

    disFilterKernel << <blocks, threadsPerBlock >> > (
        dev_movement_c,
        start,
        dev_movement_a,
        size);

    cudaError_t cudaStatus =
        cudaGetLastError();

    if (cudaStatus != cudaSuccess)
    {
        fprintf(
            stderr,
            "movement kernel launch failed: %s\n",
            cudaGetErrorString(cudaStatus));

        movementMemFreeWithCuda();

        return cudaStatus;
    }

    cudaStatus =
        cudaDeviceSynchronize();

    if (cudaStatus != cudaSuccess)
    {
        fprintf(
            stderr,
            "movement cudaDeviceSynchronize failed: %s\n",
            cudaGetErrorString(cudaStatus));

        movementMemFreeWithCuda();

        return cudaStatus;
    }

    cudaStatus = cudaMemcpy(
        c,
        dev_movement_c,
        size * sizeof(float),
        cudaMemcpyDeviceToHost);

    if (cudaStatus != cudaSuccess)
    {
        fprintf(
            stderr,
            "movement cudaMemcpy failed: %s\n",
            cudaGetErrorString(cudaStatus));

        movementMemFreeWithCuda();

        return cudaStatus;
    }

    return cudaSuccess;
}


// ============================================================
// Movement - GPU 메모리 해제
// ============================================================

int movementMemFreeWithCuda()
{
    if (dev_movement_c != nullptr)
    {
        cudaFree(dev_movement_c);
        dev_movement_c = nullptr;
    }

    if (dev_movement_a != nullptr)
    {
        cudaFree(dev_movement_a);
        dev_movement_a = nullptr;
    }

    return 0;
}

// Helper function for using CUDA to add vectors in parallel.
/*cudaError_t disFilterWithCuda(float4* c, const float4 start, const float4* a, unsigned int size)
{
    float4* dev_c;
    float4* dev_a;

    cudaError_t cudaStatus;

    // Choose which GPU to run on, change this on a multi-GPU system.
    cudaStatus = cudaSetDevice(0);
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaSetDevice failed!  Do you have a CUDA-capable GPU installed?");
        goto Error;
    }

    // Allocate GPU buffers for three vectors (two input, one output)    .
    cudaStatus = cudaMalloc((void**)&dev_c, size * sizeof(float4));
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaMalloc failed!");
        goto Error;
    }

    cudaStatus = cudaMalloc((void**)&dev_a, size * sizeof(float4));
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaMalloc failed!");
        goto Error;
    }

    // gpu버퍼로 입력
    cudaStatus = cudaMemcpy(dev_a, a, size * sizeof(float4), cudaMemcpyHostToDevice);
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaMemcpy failed!");
        goto Error;
    }

    // 커널실행
    disFilterKernel << <1, size >> > (dev_c, start, dev_a);

    // Check for any errors launching the kernel
    cudaStatus = cudaGetLastError();
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "addKernel launch failed: %s\n", cudaGetErrorString(cudaStatus));
        goto Error;
    }

    // cudaDeviceSynchronize waits for the kernel to finish, and returns
    // any errors encountered during the launch.
    cudaStatus = cudaDeviceSynchronize();
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaDeviceSynchronize returned error code %d after launching addKernel!\n", cudaStatus);
        goto Error;
    }

    // 출력결과 메모리로 복사
    cudaStatus = cudaMemcpy(c, dev_c, size * sizeof(float4), cudaMemcpyDeviceToHost);
    if (cudaStatus != cudaSuccess) {
        fprintf(stderr, "cudaMemcpy failed!");
        goto Error;
    }

    //메모리 해제
Error:
    cudaFree(dev_c);
    cudaFree(dev_a);

    return cudaStatus;
}*/
